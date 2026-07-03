using System.ComponentModel.DataAnnotations;

namespace Wec.Modules.Diagnostics;

public sealed class DiagnosticsOptions
{
    public const string SectionName = "Wec:Diagnostics";

    [Required]
    public string DnsProbeHostname { get; set; } = string.Empty;

    public TimeSpan ProbeTimeout { get; set; } = TimeSpan.FromSeconds(3);

    public string[] EventLogNames { get; set; } = [];

    public TimeSpan EventLogLookback { get; set; } = TimeSpan.FromHours(24);

    [Range(1, 10_000)]
    public int EventLogMaxEntries { get; set; } = 500;

    [Range(1, 10_000)]
    public int EventLogErrorWarningThreshold { get; set; } = 50;

    public string[] MonitoredServices { get; set; } = [];

    [Range(1, 99)]
    public int MinimumFreeDiskSpacePercent { get; set; } = 10;

    [Range(1, 730)]
    public int MaxDaysSinceLastInstalledUpdate { get; set; } = 60;
}
