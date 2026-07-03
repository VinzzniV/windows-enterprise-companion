using Microsoft.Extensions.DependencyInjection;
using Wec.Core.Messaging;
using Wec.Core.Modules;
using Wec.Modules.PatchManagement.Application;
using Wec.Modules.PatchManagement.Handlers;
using Wec.Modules.PatchManagement.Persistence;

namespace Wec.Modules.PatchManagement;

public sealed class PatchManagementModule : IModule
{
    public ModuleDescriptor Descriptor { get; } = new("patchmanagement", "Patch Management");

    public void RegisterServices(IServiceCollection services)
    {
        // Session credentials live for the process lifetime at most (ADR 0008)
        services.AddSingleton<OpsiSessionState>();
        services.AddScoped<IPatchMappingRepository, EfPatchMappingRepository>();
        services.AddScoped<PatchDashboardService>();
        services.AddScoped<IActionHandler, ConnectOpsiHandler>();
        services.AddScoped<IActionHandler, DisconnectOpsiHandler>();
        services.AddScoped<IActionHandler, GetOpsiConnectionStatusHandler>();
        services.AddScoped<IActionHandler, GetPatchDashboardHandler>();
    }
}
