namespace Wec.Modules.Inventory.Domain;

/// <summary>
/// The optional sections stay null when they were not captured (snapshots
/// cached by older versions). A null software list with a non-null
/// <see cref="InstalledSoftwareError"/> means the capture was attempted and
/// failed — never silently show an empty list for that case.
/// </summary>
public sealed record HardwareSnapshot(
    CpuInfo Cpu,
    IReadOnlyList<MemoryBank> MemoryBanks,
    IReadOnlyList<DiskDrive> Disks,
    OperatingSystemInfo OperatingSystem,
    IReadOnlyList<PhysicalNetworkAdapter>? NetworkAdapters = null,
    IReadOnlyList<GpuInfo>? Gpus = null,
    IReadOnlyList<MonitorInfo>? Monitors = null,
    IReadOnlyList<InstalledSoftwareEntry>? InstalledSoftware = null,
    SoftwareCaptureError? InstalledSoftwareError = null);

/// <summary>Why the installed-software capture failed (wire-format error code + message).</summary>
public sealed record SoftwareCaptureError(string Code, string Message);
