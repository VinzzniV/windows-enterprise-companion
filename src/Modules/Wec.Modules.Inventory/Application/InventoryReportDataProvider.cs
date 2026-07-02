using Wec.Core.Contracts;
using Wec.Modules.Inventory.Domain;
using Wec.Modules.Inventory.Persistence;

namespace Wec.Modules.Inventory.Application;

/// <summary>Read contract implementation for consumers outside this module (ADR 0004).</summary>
internal sealed class InventoryReportDataProvider : IInventoryReportDataProvider
{
    private readonly IHardwareSnapshotRepository _repository;

    public InventoryReportDataProvider(IHardwareSnapshotRepository repository)
    {
        _repository = repository;
    }

    public async Task<InventoryReportData?> GetLatestAsync(CancellationToken cancellationToken)
    {
        CachedHardwareSnapshot? cached = await _repository.GetLatestAsync(cancellationToken);
        if (cached is null)
        {
            return null;
        }

        HardwareSnapshot snapshot = cached.Snapshot;
        return new InventoryReportData(
            cached.CapturedAtUtc,
            new CpuReportData(
                snapshot.Cpu.Name,
                snapshot.Cpu.PhysicalCores,
                snapshot.Cpu.LogicalProcessors,
                snapshot.Cpu.MaxClockSpeedMhz),
            snapshot.MemoryBanks
                .Select(bank => new MemoryBankReportData(
                    bank.Manufacturer, bank.PartNumber, bank.CapacityBytes, bank.SpeedMtps))
                .ToList(),
            snapshot.Disks
                .Select(disk => new DiskReportData(disk.Model, disk.SizeBytes, disk.InterfaceType))
                .ToList(),
            new OperatingSystemReportData(
                snapshot.OperatingSystem.Caption,
                snapshot.OperatingSystem.Version,
                snapshot.OperatingSystem.BuildNumber,
                snapshot.OperatingSystem.Architecture));
    }
}
