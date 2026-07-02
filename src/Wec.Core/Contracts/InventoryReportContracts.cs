namespace Wec.Core.Contracts;

public sealed record CpuReportData(string Name, int PhysicalCores, int LogicalProcessors, int MaxClockSpeedMhz);

public sealed record MemoryBankReportData(string? Manufacturer, string? PartNumber, long CapacityBytes, int? SpeedMtps);

public sealed record DiskReportData(string Model, long SizeBytes, string? InterfaceType);

public sealed record OperatingSystemReportData(string Caption, string Version, string BuildNumber, string? Architecture);

public sealed record InventoryReportData(
    DateTimeOffset CapturedAtUtc,
    CpuReportData Cpu,
    IReadOnlyList<MemoryBankReportData> MemoryBanks,
    IReadOnlyList<DiskReportData> Disks,
    OperatingSystemReportData OperatingSystem);

/// <summary>Implemented by the Inventory module; consumed via Core only (ADR 0004).</summary>
public interface IInventoryReportDataProvider
{
    Task<InventoryReportData?> GetLatestAsync(CancellationToken cancellationToken);
}
