using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.PatchManagement.Application;

namespace Wec.Modules.PatchManagement.Handlers;

public sealed record GetPatchDashboardRequest(string? DepotFilter = null);

internal sealed class GetPatchDashboardHandler
    : IActionHandler<GetPatchDashboardRequest, PatchDashboardOverview>
{
    private readonly PatchDashboardService _dashboardService;
    private readonly PatchDashboardSnapshotCache _snapshotCache;
    private readonly OpsiSessionState _sessionState;

    public GetPatchDashboardHandler(
        PatchDashboardService dashboardService,
        PatchDashboardSnapshotCache snapshotCache,
        OpsiSessionState sessionState)
    {
        _dashboardService = dashboardService;
        _snapshotCache = snapshotCache;
        _sessionState = sessionState;
    }

    public string Module => "patchmanagement";

    public string Action => "getDashboard";

    public async Task<Result<PatchDashboardOverview>> HandleAsync(
        GetPatchDashboardRequest payload, CancellationToken cancellationToken)
    {
        Result<PatchDashboardResult> dashboard =
            await _dashboardService.GetDashboardAsync(payload.DepotFilter, cancellationToken);
        if (dashboard.IsFailure)
        {
            return Result.Failure<PatchDashboardOverview>(dashboard.Error!);
        }

        if (_sessionState.Current is not { } session)
        {
            return Result.Failure<PatchDashboardOverview>(PatchDashboardService.NotConnected);
        }

        _snapshotCache.Store(
            PatchDashboardSnapshotIdentity.From(session, dashboard.Value.DepotFilter),
            dashboard.Value);
        return Result.Success(PatchDashboardPaging.Overview(dashboard.Value));
    }
}

internal sealed class ListPatchClientStatesHandler
    : IActionHandler<ListPatchClientStatesRequest, PatchClientStatePage>
{
    private static readonly Error MissingSnapshot = new(
        ErrorCode.InvalidRequest,
        "No matching Patch Management snapshot is available. Refresh the patch overview first.");

    private readonly PatchDashboardSnapshotCache _snapshotCache;
    private readonly OpsiSessionState _sessionState;

    public ListPatchClientStatesHandler(
        PatchDashboardSnapshotCache snapshotCache,
        OpsiSessionState sessionState)
    {
        _snapshotCache = snapshotCache;
        _sessionState = sessionState;
    }

    public string Module => "patchmanagement";

    public string Action => "listClientStates";

    public Task<Result<PatchClientStatePage>> HandleAsync(
        ListPatchClientStatesRequest payload,
        CancellationToken cancellationToken)
    {
        if (_sessionState.Current is not { } session)
        {
            return Task.FromResult(Result.Failure<PatchClientStatePage>(
                PatchDashboardService.NotConnected));
        }

        PatchDashboardSnapshotIdentity identity =
            PatchDashboardSnapshotIdentity.From(session, payload.DepotFilter);
        if (!_snapshotCache.TryGet(identity, out PatchDashboardResult? dashboard))
        {
            return Task.FromResult(Result.Failure<PatchClientStatePage>(MissingSnapshot));
        }

        return Task.FromResult(Result.Success(PatchDashboardPaging.Page(dashboard!, payload)));
    }
}
