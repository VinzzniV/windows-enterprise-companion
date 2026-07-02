namespace Wec.Modules.Inventory.Domain;

public sealed record OperatingSystemInfo(
    string Caption,
    string Version,
    string BuildNumber,
    string? Architecture);
