namespace Wec.Modules.Inventory;

public sealed class InventoryOptions
{
    public const string SectionName = "Wec:Inventory";

    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromMinutes(15);
}
