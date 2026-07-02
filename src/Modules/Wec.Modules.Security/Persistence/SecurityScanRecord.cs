namespace Wec.Modules.Security.Persistence;

public sealed class SecurityScanRecord
{
    public long Id { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset CompletedAtUtc { get; set; }

    public string Status { get; set; } = string.Empty;

    public int FindingCount { get; set; }

    public List<SecurityFindingRecord> Findings { get; set; } = [];
}
