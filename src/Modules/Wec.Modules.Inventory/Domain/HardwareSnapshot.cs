namespace Wec.Modules.Inventory.Domain;

/// <summary>
/// The optional sections stay null when they were not captured: snapshots
/// cached by older versions, and installed software on remote targets
/// (registry-based, local-only until the registry seam grows a remote path).
/// </summary>
public sealed record HardwareSnapshot(
    CpuInfo Cpu,
    IReadOnlyList<MemoryBank> MemoryBanks,
    IReadOnlyList<DiskDrive> Disks,
    OperatingSystemInfo OperatingSystem,
    IReadOnlyList<PhysicalNetworkAdapter>? NetworkAdapters = null,
    IReadOnlyList<GpuInfo>? Gpus = null,
    IReadOnlyList<MonitorInfo>? Monitors = null,
    IReadOnlyList<InstalledSoftwareEntry>? InstalledSoftware = null);
