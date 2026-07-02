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

    public async Task<SecurityReportData?> GetLatestScanAsync(CancellationToken cancellationToken)
    {
        SecurityScanResult? scan = await _repository.GetLatestScanAsync(cancellationToken);
        if (scan is null)
        {
            return null;
        }

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
                .ToList());
    }
}
