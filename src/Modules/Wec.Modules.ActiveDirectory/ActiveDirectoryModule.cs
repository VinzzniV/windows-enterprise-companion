using Microsoft.Extensions.DependencyInjection;
using Wec.Core.Messaging;
using Wec.Core.Modules;
using Wec.Modules.ActiveDirectory.Application;
using Wec.Modules.ActiveDirectory.Handlers;

namespace Wec.Modules.ActiveDirectory;

public sealed class ActiveDirectoryModule : IModule
{
    public ModuleDescriptor Descriptor { get; } = new("activedirectory", "Active Directory");

    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<DomainContextService>();
        services.AddScoped<DirectoryOverviewService>();
        services.AddScoped<DirectoryHygieneService>();
        services.AddScoped<IActionHandler, GetAdOverviewHandler>();
        services.AddScoped<IActionHandler, GetAdHygieneHandler>();
        services.AddScoped<IActionHandler, TestDirectoryConnectionHandler>();
    }
}
