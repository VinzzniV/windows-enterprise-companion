using Microsoft.Extensions.DependencyInjection;
using Wec.Core.Messaging;
using Wec.Core.Modules;
using Wec.Modules.DeviceCleanup.Application;
using Wec.Modules.DeviceCleanup.Handlers;

namespace Wec.Modules.DeviceCleanup;

public sealed class DeviceCleanupModule : IModule
{
    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<DeviceCleanupService>();
        services.AddScoped<IActionHandler, ListDeviceCleanupCandidatesHandler>();
        services.AddScoped<IActionHandler, ExportDeviceCleanupAssessmentHandler>();
    }
}
