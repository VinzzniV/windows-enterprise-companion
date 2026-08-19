using System.ComponentModel.DataAnnotations;
using Wec.Core.Configuration;

namespace Wec.Modules.NetworkScan;

public sealed class NetworkScanOptions : IValidatableObject
{
    public const string SectionName = "Wec:NetworkScan";

    /// <summary>Full path to nmap.exe. Null/empty = default install path, then PATH.</summary>
    public string? NmapPath { get; set; }

    /// <summary>
    /// TCP ports probed when a port scan is requested. Chosen to both spot open
    /// management surfaces and drive device typing: 9100/515/631 = printer,
    /// 80/443/8080 = web UI, 161/23 = network gear, 3389/445 = Windows, 22 = SSH.
    /// </summary>
    [MinLength(1)]
    public IReadOnlyList<int> ScanPorts { get; set; } =
        [9100, 515, 631, 80, 443, 8080, 161, 3389, 22, 445, 23];

    /// <summary>Hard ceiling for a single nmap run before it is cancelled.</summary>
    [PositiveTimeSpan]
    public TimeSpan ScanTimeout { get; set; } = TimeSpan.FromMinutes(5);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ScanPorts.Any(port => port is < 1 or > 65_535))
        {
            yield return new ValidationResult(
                "Every scan port must be between 1 and 65535.",
                [nameof(ScanPorts)]);
        }
    }
}
