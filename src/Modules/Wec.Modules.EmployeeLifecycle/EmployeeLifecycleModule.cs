using Microsoft.Extensions.DependencyInjection;
using Wec.Core.Contracts;
using Wec.Core.Messaging;
using Wec.Core.Modules;
using Wec.Modules.EmployeeLifecycle.Application;
using Wec.Modules.EmployeeLifecycle.Handlers;

namespace Wec.Modules.EmployeeLifecycle;

public sealed class EmployeeLifecycleModule : IModule
{
    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<KasperskySecurityCenterClient>();
        services.AddScoped<IKasperskyInventoryReader>(serviceProvider =>
            serviceProvider.GetRequiredService<KasperskySecurityCenterClient>());
        services.AddSingleton<ItHygieneSnapshotCache>();
        services.AddScoped<IManagementDeviceSnapshotProvider, ManagementDeviceSnapshotProvider>();
        services.AddScoped<ItHygieneService>();
        services.AddScoped<IHygieneActionEvidenceProvider, HygieneActionEvidenceProvider>();
        services.AddScoped<IDeviceCleanupEvidenceProvider, DeviceCleanupEvidenceProvider>();
        services.AddScoped<ClientOverviewService>();
        services.AddScoped<IActionHandler, GetItHygieneHandler>();
        services.AddScoped<IActionHandler, GetItHygieneOverviewHandler>();
        services.AddScoped<IActionHandler, ListHygieneDevicesHandler>();
        services.AddScoped<IActionHandler, ListClientWorkspaceHandler>();
        services.AddScoped<IActionHandler, GetClientOverviewHandler>();
    }
}
