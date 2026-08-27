using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.Inventory.Domain;

namespace Wec.Modules.Inventory.Application;

[BridgeContract]
public sealed record InventoryBatchProgress(string Host, InventoryBatchHostStatus Status);

public sealed partial class BatchInventoryService
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IBridgeEventPublisher _eventPublisher;
    private readonly IClock _clock;
    private readonly RemoteScanOptions _remoteScanOptions;
    private readonly ILogger<BatchInventoryService> _logger;

    public BatchInventoryService(
        IServiceScopeFactory serviceScopeFactory,
        IBridgeEventPublisher eventPublisher,
        IClock clock,
        IOptions<RemoteScanOptions> remoteScanOptions,
        ILogger<BatchInventoryService> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _eventPublisher = eventPublisher;
        _clock = clock;
        _remoteScanOptions = remoteScanOptions.Value;
        _logger = logger;
    }

    public async Task<Result<InventoryBatchResult>> RunAsync(
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
            return Result.Failure<InventoryBatchResult>(new Error(
                ErrorCode.InvalidRequest,
                "An Inventory batch needs at least one host."));
        }

        if (distinctHosts.Count > _remoteScanOptions.MaxBatchHosts)
        {
            return Result.Failure<InventoryBatchResult>(new Error(
                ErrorCode.InvalidRequest,
                $"An Inventory batch accepts at most {_remoteScanOptions.MaxBatchHosts} hosts."));
        }

        DateTimeOffset startedAtUtc = _clock.UtcNow;
        foreach (string host in distinctHosts)
        {
            PublishStatus(host, InventoryBatchHostStatus.Queued);
        }

        using var limiter = new SemaphoreSlim(_remoteScanOptions.MaxParallelScans);
        InventoryBatchHostOutcome[] outcomes = await Task.WhenAll(distinctHosts.Select(host =>
            ScanHostAsync(host, credentials, limiter, cancellationToken)));

        int failedHostCount = outcomes.Count(outcome => outcome.Status == InventoryBatchHostStatus.Failed);
        LogBatchFinished(outcomes.Length, failedHostCount);
        return Result.Success(new InventoryBatchResult(startedAtUtc, _clock.UtcNow, outcomes));
    }

    private async Task<InventoryBatchHostOutcome> ScanHostAsync(
        string host,
        ScanCredentials credentials,
        SemaphoreSlim limiter,
        CancellationToken cancellationToken)
    {
        await limiter.WaitAsync(cancellationToken);
        try
        {
            PublishStatus(host, InventoryBatchHostStatus.Running);
            using IServiceScope scope = _serviceScopeFactory.CreateScope();
            HardwareInfoService service = scope.ServiceProvider.GetRequiredService<HardwareInfoService>();
            Result<HardwareInfoResult> inventory = await service.GetHardwareInfoAsync(
                ScanTarget.Remote(host),
                credentials,
                forceRefresh: true,
                cancellationToken);
            if (inventory.IsFailure)
            {
                PublishStatus(host, InventoryBatchHostStatus.Failed);
                return new InventoryBatchHostOutcome(
                    host,
                    InventoryBatchHostStatus.Failed,
                    null,
                    ScanError.FromError(host, inventory.Error!));
            }

            PublishStatus(host, InventoryBatchHostStatus.Completed);
            return new InventoryBatchHostOutcome(
                host,
                InventoryBatchHostStatus.Completed,
                inventory.Value,
                null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Inventory batch scan of {Host} crashed unexpectedly", host);
            PublishStatus(host, InventoryBatchHostStatus.Failed);
            return new InventoryBatchHostOutcome(
                host,
                InventoryBatchHostStatus.Failed,
                null,
                new ScanError(
                    host,
                    ScanPhase.Query,
                    ErrorCode.InternalError,
                    "The Inventory scan crashed unexpectedly. See the application log for details."));
        }
        finally
        {
            limiter.Release();
        }
    }

    private void PublishStatus(string host, InventoryBatchHostStatus status) =>
        _eventPublisher.Publish(new BridgeEvent(
            "inventory",
            "batchScanProgress",
            new InventoryBatchProgress(host, status)));

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Inventory batch finished: {HostCount} hosts, {FailedHostCount} failed")]
    private partial void LogBatchFinished(int hostCount, int failedHostCount);
}
