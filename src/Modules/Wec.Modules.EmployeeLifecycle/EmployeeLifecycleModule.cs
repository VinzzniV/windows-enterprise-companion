using Microsoft.Extensions.DependencyInjection;
using Wec.Core.Messaging;
using Wec.Core.Modules;
using Wec.Modules.EmployeeLifecycle.Application;
using Wec.Modules.EmployeeLifecycle.Handlers;

namespace Wec.Modules.EmployeeLifecycle;

public sealed class EmployeeLifecycleModule : IModule
{
    public ModuleDescriptor Descriptor { get; } = new("employeelifecycle", "IT Lifecycle");

    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<KasperskySecurityCenterClient>();
        services.AddScoped<IKasperskyInventoryReader>(serviceProvider =>
            serviceProvider.GetRequiredService<KasperskySecurityCenterClient>());
        services.AddScoped<ItHygieneService>();
        services.AddScoped<IActionHandler, GetItHygieneHandler>();
    }
}
