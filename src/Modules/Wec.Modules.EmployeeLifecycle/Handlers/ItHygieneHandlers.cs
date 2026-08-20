using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Core.Contracts;
using Wec.Modules.EmployeeLifecycle.Application;

namespace Wec.Modules.EmployeeLifecycle.Handlers;

internal sealed class GetItHygieneHandler : IActionHandler<ItHygieneRequest, ItHygieneResult>
{
    private readonly ItHygieneService _service;
    private readonly ItHygieneSnapshotCache _cache;

    public GetItHygieneHandler(ItHygieneService service, ItHygieneSnapshotCache cache)
    {
        _service = service;
        _cache = cache;
    }

    public string Module => "employeelifecycle";

    public string Action => "getHygiene";

    public Task<Result<ItHygieneResult>> HandleAsync(
        ItHygieneRequest payload,
        CancellationToken cancellationToken) =>
        _cache.GetAsync(payload, force: false, _service.LoadAsync, cancellationToken);
}

internal sealed class GetItHygieneOverviewHandler(
    ItHygieneService service,
    ItHygieneSnapshotCache cache) : IActionHandler<GetItHygieneOverviewRequest, ItHygieneOverview>
{
    public string Module => "employeelifecycle";
    public string Action => "getHygieneOverview";

    public async Task<Result<ItHygieneOverview>> HandleAsync(
        GetItHygieneOverviewRequest payload,
        CancellationToken cancellationToken)
    {
        var request = new ItHygieneRequest(payload.ActiveDirectory, payload.Kaspersky, payload.OperationId);
        Result<ItHygieneResult> result = await cache.GetAsync(request, payload.Force, service.LoadAsync, cancellationToken);
        return result.IsFailure
            ? Result.Failure<ItHygieneOverview>(result.Error!)
            : Result.Success(ItHygienePaging.Overview(result.Value));
    }
}

internal sealed class ListHygieneDevicesHandler(
    ItHygieneService service,
    ItHygieneSnapshotCache cache) : IActionHandler<ListHygieneDevicesRequest, HygieneDevicePage>
{
    public string Module => "employeelifecycle";
    public string Action => "listHygieneDevices";

    public async Task<Result<HygieneDevicePage>> HandleAsync(
        ListHygieneDevicesRequest payload,
        CancellationToken cancellationToken)
    {
        var request = new ItHygieneRequest(payload.ActiveDirectory, payload.Kaspersky, payload.OperationId);
        Result<ItHygieneResult> result = await cache.GetAsync(request, force: false, service.LoadAsync, cancellationToken);
        return result.IsFailure
            ? Result.Failure<HygieneDevicePage>(result.Error!)
            : Result.Success(ItHygienePaging.Page(result.Value, payload));
    }
}

internal sealed class ListClientWorkspaceHandler(
    ItHygieneService service,
    ItHygieneSnapshotCache cache,
    IInventoryClientSnapshotProvider inventory,
    ISavedClientTargetProvider targets) : IActionHandler<ListClientWorkspaceRequest, ClientWorkspacePage>
{
    public string Module => "employeelifecycle";
    public string Action => "listClientWorkspace";

    public async Task<Result<ClientWorkspacePage>> HandleAsync(
        ListClientWorkspaceRequest payload,
        CancellationToken cancellationToken)
    {
        var request = new ItHygieneRequest(payload.ActiveDirectory, payload.Kaspersky, payload.OperationId);
        Result<ItHygieneResult> result = await cache.GetAsync(request, payload.Force, service.LoadAsync, cancellationToken);
        if (result.IsFailure)
        {
            return Result.Failure<ClientWorkspacePage>(result.Error!);
        }

        IReadOnlyList<InventoryClientSnapshotHost> scannedHosts = await inventory.ListHostsAsync(cancellationToken);
        IReadOnlyList<SavedClientTarget> savedClients = await targets.ListClientsAsync(cancellationToken);
        return Result.Success(ClientWorkspacePaging.Page(result.Value, scannedHosts, savedClients, payload));
    }
}
