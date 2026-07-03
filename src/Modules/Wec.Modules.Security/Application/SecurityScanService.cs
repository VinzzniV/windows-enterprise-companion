using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.Security.Domain;
using Wec.Modules.Security.Persistence;

namespace Wec.Modules.Security.Application;

public sealed record LatestScanResult(SecurityScanResult? Scan);

public sealed partial class SecurityScanService
{
    private readonly List<ISecurityCheck> _checks;
    private readonly ISecurityScanRepository _repository;
    private readonly IClock _clock;
    private readonly ConnectionOptions _connectionOptions;
    private readonly ILogger<SecurityScanService> _logger;

    public SecurityScanService(
        IEnumerable<ISecurityCheck> checks,
        ISecurityScanRepository repository,
        IClock clock,
        IOptions<RemoteScanOptions> remoteScanOptions,
        ILogger<SecurityScanService> logger)
    {
        _checks = checks.ToList();
        _repository = repository;
        _clock = clock;
        _connectionOptions = remoteScanOptions.Value.ToConnectionOptions();
        _logger = logger;
    }

    public async Task<Result<SecurityScanResult>> RunScanAsync(
        ScanTarget target,
        ScanCredentials credentials,
        CancellationToken cancellationToken)
    {
        var context = new SecurityScanContext(target, credentials, _connectionOptions);
        DateTimeOffset startedAtUtc = _clock.UtcNow;
        var findings = new List<SecurityFinding>();
        var failedChecks = 0;

        foreach (ISecurityCheck check in _checks)
        {
            try
            {
                findings.AddRange(await check.EvaluateAsync(context, cancellationToken));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                // A crashing check is a bug, but it must not take the whole scan
                // down or hide the results of the remaining checks. The degraded
                // state is reflected in the scan status instead of being swallowed.
                failedChecks++;
                _logger.LogError(exception, "Security check {CheckId} threw unexpectedly", check.CheckId);
            }
        }

        DateTimeOffset completedAtUtc = _clock.UtcNow;
        ScanStatus status = failedChecks == 0 ? ScanStatus.Completed : ScanStatus.CompletedWithErrors;

        long scanId = await _repository.SaveScanAsync(
            target.CacheKey,
            startedAtUtc,
            completedAtUtc,
            status,
            findings,
            cancellationToken);

        LogScanFinished(scanId, target.CacheKey, status, findings.Count, _checks.Count, failedChecks);

        return Result.Success(new SecurityScanResult(
            scanId, target.DisplayName, startedAtUtc, completedAtUtc, status, findings));
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Security scan {ScanId} of {HostKey} finished with status {Status}: {FindingCount} findings from {CheckCount} checks ({FailedCheckCount} crashed)")]
    private partial void LogScanFinished(long scanId, string hostKey, ScanStatus status, int findingCount, int checkCount, int failedCheckCount);

    public async Task<Result<LatestScanResult>> GetLatestScanAsync(
        ScanTarget target,
        CancellationToken cancellationToken)
    {
        SecurityScanResult? latestScan = await _repository.GetLatestScanAsync(target.CacheKey, cancellationToken);
        return Result.Success(new LatestScanResult(latestScan));
    }
}
