namespace Wec.Modules.Inventory.Domain;

public sealed record GpuInfo(
    string Name,
    long? MemoryBytes,
    string? DriverVersion);
