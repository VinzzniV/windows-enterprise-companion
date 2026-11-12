using System.ComponentModel.DataAnnotations;

namespace Wec.Modules.DeviceCleanup;

public sealed class DeviceCleanupOptions
{
    public const string SectionName = "Wec:DeviceCleanup";

    [Range(1, 3650)]
    public int InventoryStaleWarningDays { get; set; } = 30;

    [Range(100, 100_000)]
    public int MaximumSubjects { get; set; } = 25_000;

    [Range(10, 500)]
    public int MaximumPageSize { get; set; } = 100;

    [Range(100, 10_000)]
    public int ExportPingTimeoutMilliseconds { get; set; } = 800;

    [Range(1, 128)]
    public int ExportPingParallelism { get; set; } = 64;
}
