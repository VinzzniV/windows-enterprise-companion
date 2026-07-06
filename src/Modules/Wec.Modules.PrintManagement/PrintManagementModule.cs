using Microsoft.Extensions.DependencyInjection;
using Wec.Core.Messaging;
using Wec.Core.Modules;
using Wec.Modules.PrintManagement.Application;
using Wec.Modules.PrintManagement.Handlers;
using Wec.Modules.PrintManagement.Persistence;

namespace Wec.Modules.PrintManagement;

public sealed class PrintManagementModule : IModule
{
    public ModuleDescriptor Descriptor { get; } = new("printmanagement", "Print Management");

    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<IPrintSnapshotRepository, EfPrintSnapshotRepository>();
        services.AddScoped<PrintServerScanService>();
        services.AddScoped<ClientPrinterScanService>();
        services.AddScoped<IActionHandler, ScanPrintServerHandler>();
        services.AddScoped<IActionHandler, ScanClientPrintersHandler>();
        services.AddScoped<IActionHandler, ListPrintServersHandler>();
        services.AddScoped<IActionHandler, GetLatestPrintSnapshotHandler>();
        services.AddScoped<IActionHandler, GetPrintHistoryHandler>();
        services.AddScoped<IActionHandler, GetLeaseDiffHandler>();
        services.AddScoped<IActionHandler, DeletePrintServerHandler>();
        services.AddScoped<IActionHandler, GetPrintHintsHandler>();
        services.AddScoped<IActionHandler, ExportPrintCsvHandler>();
        services.AddScoped<IActionHandler, OpenDeviceWebUiHandler>();
    }
}
