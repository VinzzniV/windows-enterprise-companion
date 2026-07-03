namespace Wec.Modules.Inventory.Domain;

public sealed record InstalledSoftwareEntry(
    string Name,
    string? Version,
    string? Publisher);
