using Microsoft.Extensions.DependencyInjection;
using Wec.Core.Messaging;
using Wec.Core.Modules;
using Wec.Modules.Security.Application;
using Wec.Modules.Security.Application.Checks;
using Wec.Modules.Security.Handlers;
using Wec.Modules.Security.Persistence;

namespace Wec.Modules.Security;

public sealed class SecurityModule : IModule
{
    public ModuleDescriptor Descriptor { get; } = new("security", "Security");

    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<ISecurityScanRepository, EfSecurityScanRepository>();
        services.AddScoped<SecurityScanService>();
        services.AddScoped<ISecurityCheck, FirewallProfilesCheck>();
        services.AddScoped<IActionHandler, RunSecurityScanHandler>();
        services.AddScoped<IActionHandler, GetLatestSecurityScanHandler>();
    }
}
