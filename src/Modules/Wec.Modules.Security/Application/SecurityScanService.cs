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
        var checkResults = new List<SecurityCheckResult>(_checks.Count);

        foreach (ISecurityCheck check in _checks)
        {
            try
            {
                SecurityCheckResult checkResult = await check.EvaluateAsync(context, cancellationToken);
                if (!string.Equals(checkResult.CheckId, check.CheckId, StringComparison.Ordinal))
                {
                    _logger.LogError(
                        "Security check {ExpectedCheckId} returned result for {ActualCheckId}",
                        check.CheckId,
                        checkResult.CheckId);
                    checkResult = SecurityCheckResult.DidNotRun(
                        check.CheckId,
                        new Error(
                            ErrorCode.InternalError,
                            "The check returned an inconsistent execution result."));
                }

                checkResults.Add(checkResult);
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
                _logger.LogError(exception, "Security check {CheckId} threw unexpectedly", check.CheckId);
                checkResults.Add(SecurityCheckResult.DidNotRun(
                    check.CheckId,
                    new Error(
                        ErrorCode.InternalError,
                        "The check crashed unexpectedly. See the application log for details.")));
            }
        }

        DateTimeOffset completedAtUtc = _clock.UtcNow;
        IReadOnlyList<SecurityFinding> findings =
            [.. checkResults.SelectMany(result => result.Findings)];
        int failedChecks = checkResults.Count(result => result.Status == CheckStatus.Failed);
        ScanStatus status = failedChecks == 0 ? ScanStatus.Completed : ScanStatus.CompletedWithErrors;

        long scanId = await _repository.SaveScanAsync(
            target.CacheKey,
            startedAtUtc,
            completedAtUtc,
            status,
            SecurityCoverage.CurrentVersion,
            checkResults,
            cancellationToken);

        LogScanFinished(scanId, target.CacheKey, status, findings.Count, _checks.Count, failedChecks);

        return Result.Success(new SecurityScanResult(
            scanId,
            target.DisplayName,
            startedAtUtc,
            completedAtUtc,
            status,
            findings,
            checkResults,
            SecurityCoverage.CurrentVersion));
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
