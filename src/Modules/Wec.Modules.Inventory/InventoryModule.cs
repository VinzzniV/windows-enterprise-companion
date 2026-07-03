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
    public ModuleDescriptor Descriptor { get; } = new("inventory", "Inventory");

    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<IHardwareSnapshotRepository, EfHardwareSnapshotRepository>();
        services.AddScoped<InstalledSoftwareReader>();
        services.AddScoped<RemoteInstalledSoftwareReader>();
        services.AddScoped<HardwareInfoService>();
        services.AddScoped<DiskEncryptionService>();
        services.AddScoped<IInventoryReportDataProvider, InventoryReportDataProvider>();
        services.AddScoped<IActionHandler, GetHardwareInfoHandler>();
        services.AddScoped<IActionHandler, GetDiskEncryptionStatusHandler>();
    }
}
