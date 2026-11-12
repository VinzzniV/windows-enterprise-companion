using Microsoft.Extensions.DependencyInjection;
using Wec.Core.Messaging;
using Wec.Core.Modules;
using Wec.Modules.UserManagement.Application;
using Wec.Modules.UserManagement.Handlers;

namespace Wec.Modules.UserManagement;

public sealed class UserManagementModule : IModule
{
    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<UserManagementService>();
        services.AddScoped<ScopedUserProfileService>();
        services.AddScoped<IActionHandler, ScopedUserProfileHandler>();
        services.AddScoped<IActionHandler, ResolveUserSidHandler>();
        services.AddScoped<IActionHandler, ListUsersHandler>();
        services.AddScoped<IActionHandler, DirectoryUserListHandler>();
        services.AddScoped<IActionHandler, GetUserProfileHandler>();
        services.AddScoped<IActionHandler, ExportLeaverReviewHandler>();
    }
}
