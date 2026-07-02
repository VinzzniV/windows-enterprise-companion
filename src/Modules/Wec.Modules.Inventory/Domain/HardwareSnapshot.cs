namespace Wec.Modules.Inventory.Domain;

public sealed record HardwareSnapshot(
    CpuInfo Cpu,
    IReadOnlyList<MemoryBank> MemoryBanks,
    IReadOnlyList<DiskDrive> Disks,
    OperatingSystemInfo OperatingSystem);
