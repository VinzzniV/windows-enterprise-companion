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
        services.AddScoped<IActionHandler, GetClientOverviewHandler>();
    }
}
