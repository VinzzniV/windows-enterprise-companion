namespace Wec.Modules.Inventory.Domain;

public sealed record DiskDrive(
    string Model,
    long SizeBytes,
    string? InterfaceType,
    string? MediaType);
