namespace Wec.Modules.Reporting;

public sealed class ReportingOptions
{
    public const string SectionName = "Wec:Reporting";

    public TimeSpan MaximumInventoryAge { get; set; } = TimeSpan.FromHours(24);

    public TimeSpan MaximumSecurityScanAge { get; set; } = TimeSpan.FromHours(24);
}
