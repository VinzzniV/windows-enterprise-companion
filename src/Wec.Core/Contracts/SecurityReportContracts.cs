namespace Wec.Core.Contracts;

public sealed record SecurityFindingReportData(
    string FindingId,
    string Title,
    string Description,
    string Severity,
    int SeverityRank,
    string Category,
    string AffectedResource,
    string Recommendation,
    string? RequiredPrivilege);

public sealed record SecurityReportData(
    DateTimeOffset CompletedAtUtc,
    string Status,
    IReadOnlyList<SecurityFindingReportData> Findings);

/// <summary>Implemented by the Security module; consumed via Core only (ADR 0004).</summary>
public interface ISecurityReportDataProvider
{
    /// <param name="host">null = the local machine; otherwise the scanned remote host.</param>
    Task<SecurityReportData?> GetLatestScanAsync(string? host, CancellationToken cancellationToken);
}
