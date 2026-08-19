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

public sealed record SecurityCheckFailureReportData(
    string Code,
    string Message,
    string? RequiredPrivilege);

public sealed record SecurityCheckReportData(
    string CheckId,
    string Status,
    SecurityCheckFailureReportData? Failure);

public sealed record SecurityCoverageReportData(
    bool IsKnown,
    bool IsComplete,
    int TotalChecks,
    int ApplicableChecks,
    int SucceededChecks,
    int FailedChecks,
    int RequiresElevationChecks,
    int NotApplicableChecks);

public sealed record SecurityReportData(
    DateTimeOffset CompletedAtUtc,
    string Status,
    IReadOnlyList<SecurityFindingReportData> Findings,
    SecurityCoverageReportData Coverage,
    IReadOnlyList<SecurityCheckReportData> CheckResults);

/// <summary>Implemented by the Security module; consumed via Core only (ADR 0004).</summary>
public interface ISecurityReportDataProvider
{
    /// <param name="host">null = the local machine; otherwise the scanned remote host.</param>
    Task<SecurityReportData?> GetLatestScanAsync(string? host, CancellationToken cancellationToken);
}
