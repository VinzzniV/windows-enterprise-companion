using Microsoft.Extensions.DependencyInjection;
using Wec.Core.Messaging;
using Wec.Core.Modules;
using Wec.Modules.Targets.Handlers;
using Wec.Modules.Targets.Persistence;

namespace Wec.Modules.Targets;

public sealed class TargetsModule : IModule
{
    public ModuleDescriptor Descriptor { get; } = new("targets", "Saved Targets");

    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<ISavedTargetRepository, EfSavedTargetRepository>();
        services.AddScoped<IActionHandler, ListSavedTargetsHandler>();
        services.AddScoped<IActionHandler, SaveTargetHandler>();
        services.AddScoped<IActionHandler, DeleteSavedTargetHandler>();
    }
}
