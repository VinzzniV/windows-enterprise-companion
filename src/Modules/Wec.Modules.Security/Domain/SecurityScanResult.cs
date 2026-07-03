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
    IReadOnlyList<SecurityFinding> Findings);
