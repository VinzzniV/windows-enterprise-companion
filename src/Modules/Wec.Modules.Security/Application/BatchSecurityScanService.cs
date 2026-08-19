using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.Security.Domain;

namespace Wec.Modules.Security.Application;

[BridgeContract]
public sealed record BatchScanProgress(string Host, HostScanStatus Status);

/// <summary>
/// Scans several remote hosts in parallel (bounded by
/// Wec:Remote:MaxParallelScans). A cheap connectivity gate per host turns
/// unreachable machines into a structured per-host failure instead of a full
/// scan whose every check reports an incomplete outcome. One host failing never aborts the
/// batch. Per-host progress goes out as security/batchScanProgress events.
/// </summary>
public sealed partial class BatchSecurityScanService
{
    private const string CimV2Namespace = @"root\cimv2";
    private const string ConnectivityGateQuery = "SELECT CSName FROM Win32_OperatingSystem";

    private readonly IWmiQueryService _wmiQueryService;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IBridgeEventPublisher _eventPublisher;
    private readonly IClock _clock;
    private readonly RemoteScanOptions _remoteScanOptions;
    private readonly ILogger<BatchSecurityScanService> _logger;

    public BatchSecurityScanService(
        IWmiQueryService wmiQueryService,
        IServiceScopeFactory serviceScopeFactory,
        IBridgeEventPublisher eventPublisher,
        IClock clock,
        IOptions<RemoteScanOptions> remoteScanOptions,
        ILogger<BatchSecurityScanService> logger)
    {
        _wmiQueryService = wmiQueryService;
        _serviceScopeFactory = serviceScopeFactory;
        _eventPublisher = eventPublisher;
        _clock = clock;
        _remoteScanOptions = remoteScanOptions.Value;
        _logger = logger;
    }

    public async Task<Result<BatchScanResult>> RunBatchScanAsync(
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
            return Result.Failure<BatchScanResult>(new Error(
                ErrorCode.InvalidRequest,
                "A batch scan needs at least one host."));
        }

        DateTimeOffset startedAtUtc = _clock.UtcNow;
        foreach (string host in distinctHosts)
        {
            PublishStatus(host, HostScanStatus.Queued);
        }

        using var parallelScanLimiter = new SemaphoreSlim(_remoteScanOptions.MaxParallelScans);
        HostScanOutcome[] outcomes = await Task.WhenAll(distinctHosts.Select(host =>
            ScanHostAsync(host, credentials, parallelScanLimiter, cancellationToken)));

        int failedHostCount = outcomes.Count(outcome => outcome.Status == HostScanStatus.Failed);
        LogBatchFinished(distinctHosts.Count, failedHostCount);
        return Result.Success(new BatchScanResult(startedAtUtc, _clock.UtcNow, outcomes));
    }

    private async Task<HostScanOutcome> ScanHostAsync(
        string host,
        ScanCredentials credentials,
        SemaphoreSlim parallelScanLimiter,
        CancellationToken cancellationToken)
    {
        await parallelScanLimiter.WaitAsync(cancellationToken);
        try
        {
            var target = ScanTarget.Remote(host);
            PublishStatus(host, HostScanStatus.Connecting);

            Result<IReadOnlyList<WmiInstance>> connectivityGate = await _wmiQueryService.QueryAsync(
                target,
                credentials,
                _remoteScanOptions.ToConnectionOptions(),
                CimV2Namespace,
                ConnectivityGateQuery,
                cancellationToken);
            if (connectivityGate.IsFailure)
            {
                PublishStatus(host, HostScanStatus.Failed);
                return new HostScanOutcome(
                    host, HostScanStatus.Failed, null, ScanError.FromError(host, connectivityGate.Error!));
            }

            PublishStatus(host, HostScanStatus.Running);

            // Each host gets its own scope: SecurityScanService persists via the
            // scoped DbContext, which must not be shared across parallel scans
            using IServiceScope scanScope = _serviceScopeFactory.CreateScope();
            var scanService = scanScope.ServiceProvider.GetRequiredService<SecurityScanService>();
            Result<SecurityScanResult> scan = await scanService.RunScanAsync(target, credentials, cancellationToken);
            if (scan.IsFailure)
            {
                PublishStatus(host, HostScanStatus.Failed);
                return new HostScanOutcome(
                    host, HostScanStatus.Failed, null, ScanError.FromError(host, scan.Error!));
            }

            HostScanStatus status = scan.Value.Status == ScanStatus.Completed
                && scan.Value.Coverage.IsComplete
                ? HostScanStatus.Completed
                : HostScanStatus.CompletedWithErrors;
            PublishStatus(host, status);
            return new HostScanOutcome(host, status, scan.Value, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A crashing host scan is a bug, but it must not take the batch down
            _logger.LogError(exception, "Batch scan of {Host} crashed unexpectedly", host);
            PublishStatus(host, HostScanStatus.Failed);
            return new HostScanOutcome(
                host,
                HostScanStatus.Failed,
                null,
                new ScanError(host, ScanPhase.Query, ErrorCode.InternalError,
                    "The scan crashed unexpectedly. See the application log for details."));
        }
        finally
        {
            parallelScanLimiter.Release();
        }
    }

    private void PublishStatus(string host, HostScanStatus status) =>
        _eventPublisher.Publish(new BridgeEvent(
            "security",
            "batchScanProgress",
            new BatchScanProgress(host, status)));

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Batch security scan finished: {HostCount} hosts, {FailedHostCount} failed")]
    private partial void LogBatchFinished(int hostCount, int failedHostCount);
}
