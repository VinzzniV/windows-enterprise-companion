namespace Wec.Modules.Inventory.Domain;

public sealed record PhysicalNetworkAdapter(
    string Name,
    string? MacAddress,
    long? SpeedBitsPerSecond,
    bool? Connected,
    string? AdapterType);
