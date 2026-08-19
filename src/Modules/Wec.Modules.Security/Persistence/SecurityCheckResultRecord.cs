namespace Wec.Modules.Security.Persistence;

public sealed class SecurityCheckResultRecord
{
    public long Id { get; set; }

    public long ScanId { get; set; }

    public string CheckId { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string? FailureCode { get; set; }

    public string? FailureMessage { get; set; }

    public string? RequiredPrivilege { get; set; }
}
