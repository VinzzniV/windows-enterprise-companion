using Wec.Core.Configuration;

namespace Wec.Modules.Reporting;

public sealed class ReportingOptions
{
    public const string SectionName = "Wec:Reporting";

    [PositiveTimeSpan]
    public TimeSpan MaximumInventoryAge { get; set; } = TimeSpan.FromHours(24);

    [PositiveTimeSpan]
    public TimeSpan MaximumSecurityScanAge { get; set; } = TimeSpan.FromHours(24);
}
