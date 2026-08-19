namespace Wec.Modules.Security.Domain;

public enum ScanStatus
{
    Completed = 0,
    CompletedWithErrors = 1,
    Failed = 2,
}

public sealed record SecurityScanResult(
    long ScanId,
    string Host,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    ScanStatus Status,
    IReadOnlyList<SecurityFinding> Findings,
    IReadOnlyList<SecurityCheckResult> CheckResults,
    int? CoverageVersion)
{
    /// <summary>
    /// Compatibility constructor for scans written before check coverage was
    /// persisted. Such scans deliberately remain coverage-unknown.
    /// </summary>
    public SecurityScanResult(
        long scanId,
        string host,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        ScanStatus status,
        IReadOnlyList<SecurityFinding> findings)
        : this(scanId, host, startedAtUtc, completedAtUtc, status, findings, [], null)
    {
    }

    public SecurityCoverage Coverage => SecurityCoverage.From(CoverageVersion, CheckResults);
}
