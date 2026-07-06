using Microsoft.Extensions.DependencyInjection;
using Wec.Core.Messaging;
using Wec.Core.Modules;
using Wec.Modules.PrintManagement.Application;
using Wec.Modules.PrintManagement.Handlers;

namespace Wec.Modules.PrintManagement;

public sealed class PrintManagementModule : IModule
{
    public ModuleDescriptor Descriptor { get; } = new("printmanagement", "Print Management");

    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<PrintServerScanService>();
        services.AddScoped<IActionHandler, ScanPrintServerHandler>();
    }
}
