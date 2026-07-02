namespace Wec.Modules.Inventory.Domain;

public sealed record CpuInfo(
    string Name,
    int PhysicalCores,
    int LogicalProcessors,
    int MaxClockSpeedMhz);
