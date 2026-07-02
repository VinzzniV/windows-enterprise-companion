using System.ComponentModel.DataAnnotations;

namespace Wec.Modules.Diagnostics;

public sealed class DiagnosticsOptions
{
    public const string SectionName = "Wec:Diagnostics";

    [Required]
    public string DnsProbeHostname { get; set; } = string.Empty;

    public TimeSpan ProbeTimeout { get; set; } = TimeSpan.FromSeconds(3);
}
