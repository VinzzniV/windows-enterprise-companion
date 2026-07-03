using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.PatchManagement.Application;

namespace Wec.Modules.PatchManagement.Handlers;

public sealed record GetPatchDashboardRequest(string? DepotFilter = null);

internal sealed class GetPatchDashboardHandler
    : IActionHandler<GetPatchDashboardRequest, PatchDashboardResult>
{
    private readonly PatchDashboardService _dashboardService;

    public GetPatchDashboardHandler(PatchDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    public string Module => "patchmanagement";

    public string Action => "getDashboard";

    public Task<Result<PatchDashboardResult>> HandleAsync(
        GetPatchDashboardRequest payload, CancellationToken cancellationToken) =>
        _dashboardService.GetDashboardAsync(payload.DepotFilter, cancellationToken);
}
