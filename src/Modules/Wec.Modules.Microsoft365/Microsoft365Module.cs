using Microsoft.Extensions.DependencyInjection;
using Wec.Core.Messaging;
using Wec.Core.Microsoft365;
using Wec.Core.Modules;
using Wec.Modules.Microsoft365.Application;
using Wec.Modules.Microsoft365.Handlers;

namespace Wec.Modules.Microsoft365;

public sealed class Microsoft365Module : IModule
{
    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<Microsoft365Service>();
        services.AddSingleton<IMicrosoft365DeviceContextProvider>(provider => provider.GetRequiredService<Microsoft365Service>());
        services.AddSingleton<IMicrosoft365UserContextProvider>(provider => provider.GetRequiredService<Microsoft365Service>());
        services.AddSingleton<IMicrosoft365GroupContextProvider>(provider => provider.GetRequiredService<Microsoft365Service>());
        services.AddScoped<IActionHandler, Microsoft365StatusHandler>();
        services.AddScoped<IActionHandler, Microsoft365ConnectHandler>();
        services.AddScoped<IActionHandler, Microsoft365DisconnectHandler>();
        services.AddScoped<IActionHandler, Microsoft365ReadHandler>();
        services.AddScoped<IActionHandler, Microsoft365ContextHandler>();
    }
}
