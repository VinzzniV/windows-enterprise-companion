using System.ComponentModel.DataAnnotations;
using Wec.Core.Configuration;

namespace Wec.Modules.Inventory;

public sealed class InventoryOptions
{
    public const string SectionName = "Wec:Inventory";

    [PositiveTimeSpan]
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromMinutes(15);

    [Range(1, 500)]
    public int MaxUserProfiles { get; set; } = 100;
}
