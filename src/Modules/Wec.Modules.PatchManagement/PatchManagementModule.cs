using Microsoft.Extensions.DependencyInjection;
using Wec.Core.Messaging;
using Wec.Core.Modules;
using Wec.Core.Contracts;
using Wec.Modules.PatchManagement.Application;
using Wec.Modules.PatchManagement.Handlers;
using Wec.Modules.PatchManagement.Persistence;

namespace Wec.Modules.PatchManagement;

public sealed class PatchManagementModule : IModule
{
    public void RegisterServices(IServiceCollection services)
    {
        // The live session is process-local; its credential can be restored from the Windows vault.
        services.AddSingleton<OpsiSessionState>();
        services.AddSingleton<OpsiSessionConnector>();
        services.AddSingleton<PatchDashboardSnapshotCache>();
        services.AddScoped<IOpsiComputerInventoryProvider, OpsiComputerInventoryProvider>();
        services.AddScoped<IPatchAuditRepository, EfPatchAuditRepository>();
        services.AddScoped<IWingetManagedPackageRepository, EfWingetManagedPackageRepository>();
        services.AddScoped<PatchDashboardService>();
        services.AddScoped<WingetPackageService>();
        services.AddScoped<IActionHandler, ConnectOpsiHandler>();
        services.AddScoped<IActionHandler, DisconnectOpsiHandler>();
        services.AddScoped<IActionHandler, GetOpsiConnectionStatusHandler>();
        services.AddScoped<IActionHandler, GetPatchDashboardHandler>();
        services.AddScoped<IActionHandler, ListPatchClientStatesHandler>();
        services.AddScoped<IActionHandler, GetAuditLogHandler>();
        services.AddScoped<IActionHandler, SearchWingetPackagesHandler>();
        services.AddScoped<IActionHandler, PreviewWingetPackageHandler>();
        services.AddScoped<IActionHandler, CreateOrAdoptWingetPackageHandler>();
        services.AddScoped<IActionHandler, ListManagedWingetPackagesHandler>();
        services.AddScoped<IActionHandler, CheckWingetUpdatesHandler>();
        services.AddScoped<IActionHandler, PrepareWingetUpdatesHandler>();
        services.AddScoped<IActionHandler, ApplyWingetUpdatesHandler>();
    }
}
