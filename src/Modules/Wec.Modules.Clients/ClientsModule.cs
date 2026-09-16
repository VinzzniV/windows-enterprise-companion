using Microsoft.Extensions.DependencyInjection;
using Wec.Core.Messaging;
using Wec.Core.Modules;
using Wec.Modules.Clients.Application;
using Wec.Modules.Clients.Handlers;

namespace Wec.Modules.Clients;

public sealed class ClientsModule : IModule
{
    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<ClientOverviewService>();
        services.AddScoped<DeviceProfileService>();
        services.AddScoped<ManagementDeviceObjectService>();
        services.AddScoped<IActionHandler, ManagementDeviceObjectListsHandler>();
        services.AddScoped<IActionHandler, ManagementDeviceRecordHandler>();
        services.AddScoped<IActionHandler, DeviceProfileHandler>();
        services.AddScoped<IActionHandler, DeviceWorkspaceHandler>();
        services.AddScoped<IActionHandler, StoredObjectListsHandler>();
        services.AddScoped<IActionHandler, GetClientOverviewHandler>();
    }
}
