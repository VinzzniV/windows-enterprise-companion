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
    public ModuleDescriptor Descriptor { get; } = new("diagnostics", "Diagnostics");

    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<DiagnosticRunService>();
        services.AddScoped<IDiagnosticRunRepository, EfDiagnosticRunRepository>();
        services.AddScoped<IDiagnostic, NetworkConfigurationDiagnostic>();
        services.AddScoped<IDiagnostic, GatewayReachabilityDiagnostic>();
        services.AddScoped<IDiagnostic, DnsResolutionDiagnostic>();
        services.AddScoped<IDiagnostic, DomainMembershipDiagnostic>();
        services.AddScoped<IDiagnostic, TimeSynchronizationDiagnostic>();
        services.AddScoped<IDiagnostic, EventLogSummaryDiagnostic>();
        services.AddScoped<IDiagnostic, ServiceStatusDiagnostic>();
        services.AddScoped<IDiagnostic, DnsServerReachabilityDiagnostic>();
        services.AddScoped<IDiagnostic, DomainControllerReachabilityDiagnostic>();
        services.AddScoped<IDiagnostic, DiskFreeSpaceDiagnostic>();
        services.AddScoped<IDiagnostic, RebootPendingDiagnostic>();
        services.AddScoped<IDiagnostic, WindowsUpdateRecencyDiagnostic>();
        services.AddScoped<EventLogQueryService>();
        services.AddScoped<IActionHandler, RunDiagnosticsHandler>();
        services.AddScoped<IActionHandler, GetLatestDiagnosticsHandler>();
        services.AddScoped<IActionHandler, QueryEventLogHandler>();
    }
}
