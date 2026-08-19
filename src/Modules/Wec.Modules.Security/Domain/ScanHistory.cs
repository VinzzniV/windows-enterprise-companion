namespace Wec.Modules.Security.Domain;

public sealed record SeverityCount(FindingSeverity Severity, int Count);

public sealed record ScanSummary(
    long ScanId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    ScanStatus Status,
    int FindingCount,
    IReadOnlyList<SeverityCount> SeverityCounts,
    SecurityCoverage Coverage);

/// <summary>
/// Findings that appeared in / disappeared from the latest scan compared to
/// the one before it. Finding identity is (FindingId, AffectedResource) —
/// FindingId alone is not unique (one check can flag several resources).
/// </summary>
public sealed record ScanDiff(
    long LatestScanId,
    long PreviousScanId,
    IReadOnlyList<SecurityFinding> NewFindings,
    IReadOnlyList<SecurityFinding> ResolvedFindings,
    bool IsFullyComparable,
    IReadOnlyList<string> UncomparedCheckIds);

public sealed record ScanHistoryResult(
    IReadOnlyList<ScanSummary> Scans,
    ScanDiff? ChangesSinceLastScan);
