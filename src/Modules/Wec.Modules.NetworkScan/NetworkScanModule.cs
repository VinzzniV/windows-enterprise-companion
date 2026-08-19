using Microsoft.Extensions.DependencyInjection;
using Wec.Core.Messaging;
using Wec.Core.Modules;
using Wec.Modules.NetworkScan.Application;
using Wec.Modules.NetworkScan.Handlers;

namespace Wec.Modules.NetworkScan;

public sealed class NetworkScanModule : IModule
{
    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<NetworkScanService>();
        services.AddScoped<IActionHandler, ScanNetworkHandler>();
    }
}
