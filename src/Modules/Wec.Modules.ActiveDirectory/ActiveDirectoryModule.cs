using Microsoft.Extensions.DependencyInjection;
using Wec.Core.Contracts;
using Wec.Core.Messaging;
using Wec.Core.Modules;
using Wec.Modules.ActiveDirectory.Application;
using Wec.Modules.ActiveDirectory.Handlers;

namespace Wec.Modules.ActiveDirectory;

public sealed class ActiveDirectoryModule : IModule
{
    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<DomainContextService>();
        services.AddScoped<PrivilegedGroupResolver>();
        services.AddScoped<DirectoryOverviewService>();
        services.AddScoped<DirectoryHygieneService>();
        services.AddScoped<ComputerSearchService>();
        services.AddScoped<IDirectoryComputerReadProvider, DirectoryComputerReadService>();
        services.AddScoped<IAdComputerInventoryProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<ComputerSearchService>());
        services.AddScoped<IActionHandler, GetAdOverviewHandler>();
        services.AddScoped<IActionHandler, GetAdHygieneHandler>();
        services.AddScoped<IActionHandler, GetAdHygieneRulePageHandler>();
        services.AddScoped<IActionHandler, GetAdPrivilegedGroupMemberPageHandler>();
        services.AddScoped<IActionHandler, ExportActiveDirectoryCsvHandler>();
        services.AddScoped<IActionHandler, TestDirectoryConnectionHandler>();
        services.AddScoped<IActionHandler, SearchAdComputersHandler>();
        services.AddScoped<UserSearchService>();
        services.AddScoped<IDirectoryUserReadProvider, DirectoryUserReadService>();
        services.AddScoped<IActionHandler, SearchAdUsersHandler>();
    }
}
