using Microsoft.Extensions.DependencyInjection;
using Wec.Core.Messaging;
using Wec.Core.Modules;

namespace Wec.Modules.GroupManagement;

public sealed class GroupManagementModule : IModule
{
    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<GroupProfileService>();
        services.AddScoped<IActionHandler, GroupProfileHandler>();
        services.AddScoped<IActionHandler, ResolveGroupHandler>();
        services.AddScoped<IActionHandler, DirectoryGroupPageHandler>();
    }
}
