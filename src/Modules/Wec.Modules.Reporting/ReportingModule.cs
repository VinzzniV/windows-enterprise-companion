using Microsoft.Extensions.DependencyInjection;
using Wec.Core.Messaging;
using Wec.Core.Modules;
using Wec.Modules.Reporting.Application;
using Wec.Modules.Reporting.Handlers;

namespace Wec.Modules.Reporting;

public sealed class ReportingModule : IModule
{
    public ModuleDescriptor Descriptor { get; } = new("reporting", "Reporting");

    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<ReportExportService>();
        services.AddScoped<IActionHandler, GetReportOverviewHandler>();
        services.AddScoped<IActionHandler, ExportHtmlReportHandler>();
    }
}
