namespace Wec.Modules.Inventory.Domain;

public sealed record MemoryBank(
    string? Manufacturer,
    string? PartNumber,
    long CapacityBytes,
    int? SpeedMtps);
