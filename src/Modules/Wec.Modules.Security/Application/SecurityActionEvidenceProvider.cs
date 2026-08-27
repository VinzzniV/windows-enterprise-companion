using Wec.Core.Contracts;
using Wec.Modules.Security.Domain;
using Wec.Modules.Security.Persistence;

namespace Wec.Modules.Security.Application;

internal sealed class SecurityActionEvidenceProvider(
    ISecurityScanRepository repository) : ISecurityActionEvidenceProvider
{
    public async Task<SecurityActionEvidenceSnapshot> LoadStoredAsync(
        int maximumScans,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumScans);
        IReadOnlyList<SecurityScanResult> scans = await repository.ListLatestScansAsync(
            checked(maximumScans + 1), cancellationToken);
        bool truncated = scans.Count > maximumScans;
        List<SecurityScanResult> evaluated = scans.Take(maximumScans).ToList();
        List<SecurityActionEvidence> findings = evaluated
            .SelectMany(scan => scan.Findings.Select(finding => Project(scan, finding)))
            .OrderBy(finding => finding.SubjectKey, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(finding => SeverityRank(finding.Severity))
            .ThenBy(finding => finding.FindingId, StringComparer.Ordinal)
            .ToList();
        return new SecurityActionEvidenceSnapshot(findings, evaluated.Count, truncated);
    }

    private static SecurityActionEvidence Project(SecurityScanResult scan, SecurityFinding finding)
    {
        SecurityCoverage coverage = scan.Coverage;
        ActionEvidenceAvailability availability = coverage.IsKnown && coverage.IsComplete
            ? ActionEvidenceAvailability.Available
            : ActionEvidenceAvailability.Partial;
        string explanation = !coverage.IsKnown
            ? "Stored Security coverage predates the current coverage contract."
            : coverage.IsComplete
                ? $"All {coverage.TotalChecks} configured Security checks completed."
                : $"{coverage.SucceededChecks} of {coverage.ApplicableChecks} applicable Security checks succeeded.";
        return new SecurityActionEvidence(
            scan.Host,
            scan.Host,
            finding.FindingId,
            finding.Title,
            finding.Description,
            finding.Severity.ToString(),
            finding.Category.ToString(),
            finding.Recommendation,
            finding.CapturedAtUtc,
            scan.CompletedAtUtc,
            availability,
            explanation);
    }

    private static int SeverityRank(string severity) => severity switch
    {
        "Critical" => 4,
        "High" => 3,
        "Medium" => 2,
        "Low" => 1,
        _ => 0,
    };
}
