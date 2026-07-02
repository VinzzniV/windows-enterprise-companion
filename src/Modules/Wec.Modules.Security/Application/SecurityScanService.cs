using Microsoft.Extensions.Logging;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Security.Domain;
using Wec.Modules.Security.Persistence;

namespace Wec.Modules.Security.Application;

public sealed record LatestScanResult(SecurityScanResult? Scan);

public sealed class SecurityScanService
{
    private readonly List<ISecurityCheck> _checks;
    private readonly ISecurityScanRepository _repository;
    private readonly IClock _clock;
    private readonly ILogger<SecurityScanService> _logger;

    public SecurityScanService(
        IEnumerable<ISecurityCheck> checks,
        ISecurityScanRepository repository,
        IClock clock,
        ILogger<SecurityScanService> logger)
    {
        _checks = checks.ToList();
        _repository = repository;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<SecurityScanResult>> RunScanAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset startedAtUtc = _clock.UtcNow;
        var findings = new List<SecurityFinding>();
        var failedChecks = 0;

        foreach (ISecurityCheck check in _checks)
        {
            try
            {
                findings.AddRange(await check.EvaluateAsync(cancellationToken));
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
            startedAtUtc,
            completedAtUtc,
            status,
            findings,
            cancellationToken);

        _logger.LogInformation(
            "Security scan {ScanId} finished with status {Status}: {FindingCount} findings from {CheckCount} checks ({FailedCheckCount} crashed)",
            scanId,
            status,
            findings.Count,
            _checks.Count,
            failedChecks);

        return Result.Success(new SecurityScanResult(scanId, startedAtUtc, completedAtUtc, status, findings));
    }

    public async Task<Result<LatestScanResult>> GetLatestScanAsync(CancellationToken cancellationToken)
    {
        SecurityScanResult? latestScan = await _repository.GetLatestScanAsync(cancellationToken);
        return Result.Success(new LatestScanResult(latestScan));
    }
}
