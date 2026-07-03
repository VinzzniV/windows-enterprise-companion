using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.PatchManagement.Application;

namespace Wec.Modules.PatchManagement.Handlers;

public sealed record GetRolloutPreviewRequest(
    string ProductId,
    string? DepotFilter = null,
    IReadOnlyList<string>? ClientIds = null);

internal sealed class GetRolloutPreviewHandler : IActionHandler<GetRolloutPreviewRequest, RolloutPreview>
{
    private readonly PatchActionService _actionService;

    public GetRolloutPreviewHandler(PatchActionService actionService)
    {
        _actionService = actionService;
    }

    public string Module => "patchmanagement";

    public string Action => "getRolloutPreview";

    public Task<Result<RolloutPreview>> HandleAsync(
        GetRolloutPreviewRequest payload, CancellationToken cancellationToken) =>
        _actionService.BuildRolloutPreviewAsync(
            payload.ProductId, payload.DepotFilter, payload.ClientIds, cancellationToken);
}

public sealed record RequestRolloutRequest(
    string ProductId,
    IReadOnlyList<string> ClientIds,
    string? DepotFilter = null,
    bool Confirmed = false);

internal sealed class RequestRolloutHandler : IActionHandler<RequestRolloutRequest, RolloutRequestOutcome>
{
    private readonly PatchActionService _actionService;

    public RequestRolloutHandler(PatchActionService actionService)
    {
        _actionService = actionService;
    }

    public string Module => "patchmanagement";

    public string Action => "requestRollout";

    public Task<Result<RolloutRequestOutcome>> HandleAsync(
        RequestRolloutRequest payload, CancellationToken cancellationToken) =>
        _actionService.RequestRolloutAsync(
            payload.ProductId, payload.ClientIds, payload.DepotFilter, payload.Confirmed, cancellationToken);
}

public sealed record PreparePackagesRequest(IReadOnlyList<string> ProductIds);

internal sealed class PreparePackagesHandler : IActionHandler<PreparePackagesRequest, PreparePackagesPlan>
{
    private readonly PatchActionService _actionService;

    public PreparePackagesHandler(PatchActionService actionService)
    {
        _actionService = actionService;
    }

    public string Module => "patchmanagement";

    public string Action => "preparePackages";

    public Task<Result<PreparePackagesPlan>> HandleAsync(
        PreparePackagesRequest payload, CancellationToken cancellationToken) =>
        _actionService.PlanPackageUpdateAsync(payload.ProductIds ?? [], cancellationToken);
}
