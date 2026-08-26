using Microsoft.Extensions.DependencyInjection;
using Wec.Core.Messaging;
using Wec.Core.Modules;
using Wec.Modules.Diagnostics.Application;
using Wec.Modules.Diagnostics.Application.Diagnostics;
using Wec.Modules.Diagnostics.Handlers;
using Wec.Modules.Diagnostics.Persistence;

namespace Wec.Modules.Diagnostics;

public sealed class DiagnosticsModule : IModule
{
    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<DiagnosticRunService>();
        services.AddScoped<IDiagnosticRunRepository, EfDiagnosticRunRepository>();
        services.AddScoped<IDiagnostic, EventLogSummaryDiagnostic>();
        services.AddScoped<IDiagnostic, ServiceStatusDiagnostic>();
        services.AddScoped<IDiagnostic, DiskFreeSpaceDiagnostic>();
        services.AddScoped<IDiagnostic, WindowsUpdateRecencyDiagnostic>();
        services.AddScoped<EventLogQueryService>();
        services.AddScoped<IActionHandler, RunDiagnosticsHandler>();
        services.AddScoped<IActionHandler, GetLatestDiagnosticsHandler>();
        services.AddScoped<IActionHandler, QueryEventLogHandler>();
    }
}
