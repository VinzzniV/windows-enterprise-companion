using Wec.Core.Contracts;
using Wec.Modules.Security.Domain;
using Wec.Modules.Security.Persistence;

namespace Wec.Modules.Security.Application;

/// <summary>Read contract implementation for consumers outside this module (ADR 0004).</summary>
internal sealed class SecurityReportDataProvider : ISecurityReportDataProvider
{
    private readonly ISecurityScanRepository _repository;

    public SecurityReportDataProvider(ISecurityScanRepository repository)
    {
        _repository = repository;
    }

    public async Task<SecurityReportData?> GetLatestScanAsync(string? host, CancellationToken cancellationToken)
    {
        // null host = the machine WEC runs on; otherwise the scanned remote client's scan
        string cacheKey = host is null
            ? Wec.Core.Targets.ScanTarget.Local.CacheKey
            : Wec.Core.Targets.ScanTarget.Remote(host).CacheKey;
        SecurityScanResult? scan = await _repository.GetLatestScanAsync(cacheKey, cancellationToken);
        if (scan is null)
        {
            return null;
        }

        SecurityCoverage coverage = scan.Coverage;
        return new SecurityReportData(
            scan.CompletedAtUtc,
            scan.Status.ToString(),
            scan.Findings
                .Select(finding => new SecurityFindingReportData(
                    finding.FindingId,
                    finding.Title,
                    finding.Description,
                    finding.Severity.ToString(),
                    (int)finding.Severity,
                    finding.Category.ToString(),
                    finding.AffectedResource,
                    finding.Recommendation,
                    finding.RequiredPrivilege?.ToString()))
                .ToList(),
            new SecurityCoverageReportData(
                coverage.IsKnown,
                coverage.IsComplete,
                coverage.TotalChecks,
                coverage.ApplicableChecks,
                coverage.SucceededChecks,
                coverage.FailedChecks,
                coverage.RequiresElevationChecks,
                coverage.NotApplicableChecks),
            scan.CheckResults
                .Select(result => new SecurityCheckReportData(
                    result.CheckId,
                    result.Status.ToString(),
                    result.Failure is null
                        ? null
                        : new SecurityCheckFailureReportData(
                            result.Failure.Code.ToString(),
                            result.Failure.Message,
                            result.Failure.RequiredPrivilege?.ToString())))
                .ToList());
    }
}
