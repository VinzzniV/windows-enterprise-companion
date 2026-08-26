using Wec.Core.Configuration;

namespace Wec.Modules.EmployeeLifecycle;

public sealed class ClientOverviewOptions
{
    public const string SectionName = "Wec:ClientOverview";

    [PositiveTimeSpan]
    public TimeSpan MaximumInventoryAge { get; set; } = TimeSpan.FromHours(24);

    [PositiveTimeSpan]
    public TimeSpan MaximumSoftwareAge { get; set; } = TimeSpan.FromHours(24);

    [PositiveTimeSpan]
    public TimeSpan MaximumHealthAge { get; set; } = TimeSpan.FromHours(24);

    [PositiveTimeSpan]
    public TimeSpan MaximumSecurityAge { get; set; } = TimeSpan.FromHours(24);
}
