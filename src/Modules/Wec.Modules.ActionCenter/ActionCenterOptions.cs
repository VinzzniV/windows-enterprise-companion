using System.ComponentModel.DataAnnotations;

namespace Wec.Modules.ActionCenter;

public sealed class ActionCenterOptions
{
    public const string SectionName = "Wec:ActionCenter";

    [Range(1, 5000)]
    public int MaximumSecurityScans { get; set; } = 1000;

    [Range(100, 100_000)]
    public int MaximumComputedItems { get; set; } = 25_000;

    [Range(1, 3650)]
    public int InventoryStaleWarningDays { get; set; } = 30;

    [Range(10, 500)]
    public int MaximumPageSize { get; set; } = 100;
}
