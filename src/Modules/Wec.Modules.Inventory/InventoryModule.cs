using Microsoft.Extensions.DependencyInjection;
using Wec.Core.Contracts;
using Wec.Core.Messaging;
using Wec.Core.Modules;
using Wec.Modules.Inventory.Application;
using Wec.Modules.Inventory.Handlers;
using Wec.Modules.Inventory.Persistence;

namespace Wec.Modules.Inventory;

public sealed class InventoryModule : IModule
{
    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<IHardwareSnapshotRepository, EfHardwareSnapshotRepository>();
        services.AddScoped<InstalledSoftwareReader>();
        services.AddScoped<RemoteInstalledSoftwareReader>();
        services.AddScoped<DeviceUserEvidenceCollector>();
        services.AddScoped<HardwareInfoService>();
        services.AddScoped<BatchInventoryService>();
        services.AddScoped<DiskEncryptionService>();
        services.AddScoped<IInventoryReportDataProvider, InventoryReportDataProvider>();
        services.AddScoped<IInventoryClientSnapshotProvider, InventoryClientSnapshotProvider>();
        services.AddScoped<IInstalledSoftwareInventoryProvider, InstalledSoftwareInventoryProvider>();
        services.AddScoped<IUserDeviceRelationshipProvider, InventoryUserDeviceRelationshipProvider>();
        services.AddScoped<IClientUserRelationshipProvider, InventoryClientUserRelationshipProvider>();
        services.AddScoped<IDeviceCleanupInventoryEvidenceProvider, DeviceCleanupInventoryEvidenceProvider>();
        services.AddScoped<IActionHandler, GetHardwareInfoHandler>();
        services.AddScoped<IActionHandler, RunBatchInventoryHandler>();
        services.AddScoped<IActionHandler, GetDiskEncryptionStatusHandler>();
        services.AddScoped<IActionHandler, ListInventoryHostsHandler>();
        services.AddScoped<IActionHandler, DeleteHostSnapshotHandler>();
    }
}
