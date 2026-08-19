namespace Wec.Modules.Security.Persistence;

public sealed class SecurityFindingRecord
{
    public long Id { get; set; }

    public long ScanId { get; set; }

    public string? CheckId { get; set; }

    public string FindingId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Severity { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string AffectedResource { get; set; } = string.Empty;

    public string EvidenceJson { get; set; } = string.Empty;

    public string Recommendation { get; set; } = string.Empty;

    public string? RequiredPrivilege { get; set; }

    public DateTimeOffset CapturedAtUtc { get; set; }
}
