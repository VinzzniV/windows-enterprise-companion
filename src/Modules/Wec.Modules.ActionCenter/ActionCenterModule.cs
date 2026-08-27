using Microsoft.Extensions.DependencyInjection;
using Wec.Core.Messaging;
using Wec.Core.Modules;
using Wec.Modules.ActionCenter.Application;
using Wec.Modules.ActionCenter.Handlers;

namespace Wec.Modules.ActionCenter;

public sealed class ActionCenterModule : IModule
{
    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<ActionCenterService>();
        services.AddScoped<IActionHandler, ListActionCenterItemsHandler>();
    }
}
