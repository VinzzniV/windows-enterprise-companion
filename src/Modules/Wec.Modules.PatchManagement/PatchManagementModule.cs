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
    public ModuleDescriptor Descriptor { get; } = new("patchmanagement", "Patch Management");

    public void RegisterServices(IServiceCollection services)
    {
        // The live session is process-local; its credential can be restored from the Windows vault.
        services.AddSingleton<OpsiSessionState>();
        services.AddSingleton<OpsiSessionConnector>();
        services.AddScoped<IOpsiComputerInventoryProvider, OpsiComputerInventoryProvider>();
        services.AddScoped<IPatchMappingRepository, EfPatchMappingRepository>();
        services.AddScoped<IPatchAuditRepository, EfPatchAuditRepository>();
        services.AddScoped<IProductVersionSourceRepository, EfProductVersionSourceRepository>();
        services.AddScoped<PatchDashboardService>();
        services.AddScoped<PatchActionService>();
        services.AddScoped<ManufacturerVersionService>();
        services.AddScoped<IActionHandler, ConnectOpsiHandler>();
        services.AddScoped<IActionHandler, DisconnectOpsiHandler>();
        services.AddScoped<IActionHandler, GetOpsiConnectionStatusHandler>();
        services.AddScoped<IActionHandler, GetPatchDashboardHandler>();
        services.AddScoped<IActionHandler, GetRolloutPreviewHandler>();
        services.AddScoped<IActionHandler, RequestRolloutHandler>();
        services.AddScoped<IActionHandler, PreparePackagesHandler>();
        services.AddScoped<IActionHandler, ExecutePackageUpdateHandler>();
        services.AddScoped<IActionHandler, ApprovePackagePilotHandler>();
        services.AddScoped<IActionHandler, GetPackageWorkflowStatusHandler>();
        services.AddScoped<IActionHandler, GetAuditLogHandler>();
        services.AddScoped<IActionHandler, ListMappingsHandler>();
        services.AddScoped<IActionHandler, SaveMappingHandler>();
        services.AddScoped<IActionHandler, DeleteMappingHandler>();
        services.AddScoped<IActionHandler, ListVersionSourcesHandler>();
        services.AddScoped<IActionHandler, SaveVersionSourceHandler>();
        services.AddScoped<IActionHandler, DeleteVersionSourceHandler>();
        services.AddScoped<IActionHandler, CheckVendorVersionsHandler>();
    }
}
