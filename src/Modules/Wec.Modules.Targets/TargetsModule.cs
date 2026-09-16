using Microsoft.Extensions.DependencyInjection;
using Wec.Core.Contracts;
using Wec.Core.Messaging;
using Wec.Core.Modules;
using Wec.Modules.Targets.Application;
using Wec.Modules.Targets.Handlers;
using Wec.Modules.Targets.Persistence;

namespace Wec.Modules.Targets;

public sealed class TargetsModule : IModule
{
    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<IStoredDeviceListProvider, SavedClientListProvider>();
        services.AddScoped<ISavedTargetRepository, EfSavedTargetRepository>();
        services.AddScoped<ISavedClientTargetProvider, SavedClientTargetProvider>();
        services.AddScoped<IActionHandler, ListSavedTargetsHandler>();
        services.AddScoped<IActionHandler, SaveTargetHandler>();
        services.AddScoped<IActionHandler, DeleteSavedTargetHandler>();
    }
}
