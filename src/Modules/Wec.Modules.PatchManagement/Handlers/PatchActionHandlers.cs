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

public sealed record PreparePackagesRequest(
    string ProductId,
    string Stage,
    IReadOnlyList<string> DepotIds);

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
        _actionService.BuildPackageUpdatePreviewAsync(
            payload.ProductId,
            payload.Stage,
            payload.DepotIds ?? [],
            cancellationToken);
}

public sealed record ExecutePackageUpdateRequest(
    string ProductId,
    string Stage,
    IReadOnlyList<string> DepotIds,
    bool Confirmed = false);

internal sealed class ExecutePackageUpdateHandler : IActionHandler<ExecutePackageUpdateRequest, PackageUpdateOutcome>
{
    private readonly PatchActionService _actionService;

    public ExecutePackageUpdateHandler(PatchActionService actionService)
    {
        _actionService = actionService;
    }

    public string Module => "patchmanagement";

    public string Action => "executePackageUpdate";

    public Task<Result<PackageUpdateOutcome>> HandleAsync(
        ExecutePackageUpdateRequest payload,
        CancellationToken cancellationToken) =>
        _actionService.ExecutePackageUpdateAsync(
            payload.ProductId,
            payload.Stage,
            payload.DepotIds ?? [],
            payload.Confirmed,
            cancellationToken);
}

public sealed record ApprovePackagePilotRequest(string ProductId, bool Confirmed = false);

internal sealed class ApprovePackagePilotHandler : IActionHandler<ApprovePackagePilotRequest, PackageWorkflowStatus>
{
    private readonly PatchActionService _actionService;

    public ApprovePackagePilotHandler(PatchActionService actionService)
    {
        _actionService = actionService;
    }

    public string Module => "patchmanagement";

    public string Action => "approvePackagePilot";

    public Task<Result<PackageWorkflowStatus>> HandleAsync(
        ApprovePackagePilotRequest payload,
        CancellationToken cancellationToken) =>
        _actionService.ApprovePackagePilotAsync(payload.ProductId, payload.Confirmed, cancellationToken);
}

public sealed record GetPackageWorkflowStatusRequest(string ProductId);

internal sealed class GetPackageWorkflowStatusHandler
    : IActionHandler<GetPackageWorkflowStatusRequest, PackageWorkflowStatus>
{
    private readonly PatchActionService _actionService;

    public GetPackageWorkflowStatusHandler(PatchActionService actionService)
    {
        _actionService = actionService;
    }

    public string Module => "patchmanagement";

    public string Action => "getPackageWorkflowStatus";

    public async Task<Result<PackageWorkflowStatus>> HandleAsync(
        GetPackageWorkflowStatusRequest payload,
        CancellationToken cancellationToken) =>
        Result.Success(await _actionService.GetPackageWorkflowStatusAsync(
            payload.ProductId, cancellationToken));
}
