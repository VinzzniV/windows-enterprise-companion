namespace Wec.Modules.NetworkScan;

public sealed class NetworkScanOptions
{
    public const string SectionName = "Wec:NetworkScan";

    /// <summary>Full path to nmap.exe. Null/empty = default install path, then PATH.</summary>
    public string? NmapPath { get; set; }

    /// <summary>
    /// TCP ports probed when a port scan is requested. Chosen to both spot open
    /// management surfaces and drive device typing: 9100/515/631 = printer,
    /// 80/443/8080 = web UI, 161/23 = network gear, 3389/445 = Windows, 22 = SSH.
    /// </summary>
    public IReadOnlyList<int> ScanPorts { get; set; } =
        [9100, 515, 631, 80, 443, 8080, 161, 3389, 22, 445, 23];

    /// <summary>Hard ceiling for a single nmap run before it is cancelled.</summary>
    public TimeSpan ScanTimeout { get; set; } = TimeSpan.FromMinutes(5);
}
