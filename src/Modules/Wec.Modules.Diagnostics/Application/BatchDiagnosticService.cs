using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.Diagnostics.Domain;
using Wec.Modules.Diagnostics.Persistence;

namespace Wec.Modules.Diagnostics.Application;

[BridgeContract]
public sealed record DiagnosticBatchProgress(string Host, DiagnosticBatchHostStatus Status);

public sealed partial class BatchDiagnosticService
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IBridgeEventPublisher _eventPublisher;
    private readonly IClock _clock;
    private readonly RemoteScanOptions _remoteScanOptions;
    private readonly ILogger<BatchDiagnosticService> _logger;

    public BatchDiagnosticService(
        IServiceScopeFactory serviceScopeFactory,
        IBridgeEventPublisher eventPublisher,
        IClock clock,
        IOptions<RemoteScanOptions> remoteScanOptions,
        ILogger<BatchDiagnosticService> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _eventPublisher = eventPublisher;
        _clock = clock;
        _remoteScanOptions = remoteScanOptions.Value;
        _logger = logger;
    }

    public async Task<Result<DiagnosticBatchResult>> RunAsync(
        IReadOnlyList<string> hosts,
        ScanCredentials credentials,
        CancellationToken cancellationToken)
    {
        List<string> distinctHosts = hosts
            .Select(host => host.Trim())
            .Where(host => host.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (distinctHosts.Count == 0)
        {
            return Result.Failure<DiagnosticBatchResult>(new Error(
                ErrorCode.InvalidRequest,
                "A Health batch needs at least one host."));
        }

        if (distinctHosts.Count > _remoteScanOptions.MaxBatchHosts)
        {
            return Result.Failure<DiagnosticBatchResult>(new Error(
                ErrorCode.InvalidRequest,
                $"A Health batch accepts at most {_remoteScanOptions.MaxBatchHosts} hosts."));
        }

        DateTimeOffset startedAtUtc = _clock.UtcNow;
        foreach (string host in distinctHosts)
        {
            PublishStatus(host, DiagnosticBatchHostStatus.Queued);
        }

        using var limiter = new SemaphoreSlim(_remoteScanOptions.MaxParallelScans);
        DiagnosticBatchHostOutcome[] outcomes = await Task.WhenAll(distinctHosts.Select(host =>
            RunHostAsync(host, credentials, limiter, cancellationToken)));

        int failedHostCount = outcomes.Count(outcome => outcome.Status == DiagnosticBatchHostStatus.Failed);
        LogBatchFinished(outcomes.Length, failedHostCount);
        return Result.Success(new DiagnosticBatchResult(startedAtUtc, _clock.UtcNow, outcomes));
    }

    private async Task<DiagnosticBatchHostOutcome> RunHostAsync(
        string host,
        ScanCredentials credentials,
        SemaphoreSlim limiter,
        CancellationToken cancellationToken)
    {
        await limiter.WaitAsync(cancellationToken);
        try
        {
            PublishStatus(host, DiagnosticBatchHostStatus.Running);
            using IServiceScope scope = _serviceScopeFactory.CreateScope();
            DiagnosticRunService service = scope.ServiceProvider.GetRequiredService<DiagnosticRunService>();
            IDiagnosticRunRepository repository = scope.ServiceProvider.GetRequiredService<IDiagnosticRunRepository>();
            ScanTarget target = ScanTarget.Remote(host);
            var context = new DiagnosticContext(
                target,
                credentials,
                _remoteScanOptions.ToConnectionOptions());
            Result<DiagnosticRunResult> run = await service.RunAsync(context, cancellationToken);
            if (run.IsFailure)
            {
                PublishStatus(host, DiagnosticBatchHostStatus.Failed);
                return new DiagnosticBatchHostOutcome(
                    host,
                    DiagnosticBatchHostStatus.Failed,
                    null,
                    ScanError.FromError(host, run.Error!));
            }

            await repository.SaveAsync(target.CacheKey, run.Value, cancellationToken);
            PublishStatus(host, DiagnosticBatchHostStatus.Completed);
            return new DiagnosticBatchHostOutcome(
                host,
                DiagnosticBatchHostStatus.Completed,
                run.Value,
                null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Health batch run for {Host} crashed unexpectedly", host);
            PublishStatus(host, DiagnosticBatchHostStatus.Failed);
            return new DiagnosticBatchHostOutcome(
                host,
                DiagnosticBatchHostStatus.Failed,
                null,
                new ScanError(
                    host,
                    ScanPhase.Query,
                    ErrorCode.InternalError,
                    "The Health run crashed unexpectedly. See the application log for details."));
        }
        finally
        {
            limiter.Release();
        }
    }

    private void PublishStatus(string host, DiagnosticBatchHostStatus status) =>
        _eventPublisher.Publish(new BridgeEvent(
            "diagnostics",
            "batchRunProgress",
            new DiagnosticBatchProgress(host, status)));

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Health batch finished: {HostCount} hosts, {FailedHostCount} failed")]
    private partial void LogBatchFinished(int hostCount, int failedHostCount);
}
