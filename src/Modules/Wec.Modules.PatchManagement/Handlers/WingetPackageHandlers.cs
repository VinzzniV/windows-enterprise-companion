using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Core.SoftwareUpdates;
using Wec.Modules.PatchManagement.Application;

namespace Wec.Modules.PatchManagement.Handlers;

public sealed record SearchWingetPackagesRequest(string Query, int Limit = 25);
public sealed record SearchWingetPackagesResult(IReadOnlyList<WingetPackageInfo> Packages);

internal sealed class SearchWingetPackagesHandler
    : IActionHandler<SearchWingetPackagesRequest, SearchWingetPackagesResult>
{
    private readonly WingetPackageService _service;
    public SearchWingetPackagesHandler(WingetPackageService service) => _service = service;
    public string Module => "patchmanagement";
    public string Action => "searchWingetPackages";

    public async Task<Result<SearchWingetPackagesResult>> HandleAsync(
        SearchWingetPackagesRequest payload,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<WingetPackageInfo>> result = await _service.SearchAsync(
            payload.Query,
            payload.Limit,
            cancellationToken);
        return result.IsSuccess
            ? Result.Success(new SearchWingetPackagesResult(result.Value))
            : Result.Failure<SearchWingetPackagesResult>(result.Error!);
    }
}

public sealed record PreviewWingetPackageRequest(
    string OpsiProductId,
    string DisplayName,
    string WingetId,
    string DepotId);

internal sealed class PreviewWingetPackageHandler
    : IActionHandler<PreviewWingetPackageRequest, WingetPackagePreview>
{
    private readonly WingetPackageService _service;
    public PreviewWingetPackageHandler(WingetPackageService service) => _service = service;
    public string Module => "patchmanagement";
    public string Action => "previewWingetPackage";
    public Task<Result<WingetPackagePreview>> HandleAsync(
        PreviewWingetPackageRequest payload,
        CancellationToken cancellationToken) => _service.PreviewAsync(
            payload.OpsiProductId,
            payload.DisplayName,
            payload.WingetId,
            payload.DepotId,
            cancellationToken);
}

public sealed record CreateOrAdoptWingetPackageRequest(
    string OpsiProductId,
    string DisplayName,
    string WingetId,
    string DepotId,
    string ExpectedWingetVersion,
    bool Confirmed = false);

internal sealed class CreateOrAdoptWingetPackageHandler
    : IActionHandler<CreateOrAdoptWingetPackageRequest, WingetPackageOperationOutcome>
{
    private readonly WingetPackageService _service;
    public CreateOrAdoptWingetPackageHandler(WingetPackageService service) => _service = service;
    public string Module => "patchmanagement";
    public string Action => "createOrAdoptWingetPackage";
    public Task<Result<WingetPackageOperationOutcome>> HandleAsync(
        CreateOrAdoptWingetPackageRequest payload,
        CancellationToken cancellationToken) => _service.CreateOrAdoptAsync(
            payload.OpsiProductId,
            payload.DisplayName,
            payload.WingetId,
            payload.DepotId,
            payload.ExpectedWingetVersion,
            payload.Confirmed,
            cancellationToken);
}

public sealed record ListManagedWingetPackagesRequest;
public sealed record ManagedWingetPackagesResult(IReadOnlyList<WingetManagedPackageView> Packages);

internal sealed class ListManagedWingetPackagesHandler
    : IActionHandler<ListManagedWingetPackagesRequest, ManagedWingetPackagesResult>
{
    private readonly WingetPackageService _service;
    public ListManagedWingetPackagesHandler(WingetPackageService service) => _service = service;
    public string Module => "patchmanagement";
    public string Action => "listManagedWingetPackages";

    public async Task<Result<ManagedWingetPackagesResult>> HandleAsync(
        ListManagedWingetPackagesRequest payload,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<WingetManagedPackageView>> result = await _service.ListAsync(cancellationToken);
        return result.IsSuccess
            ? Result.Success(new ManagedWingetPackagesResult(result.Value))
            : Result.Failure<ManagedWingetPackagesResult>(result.Error!);
    }
}

public sealed record CheckWingetUpdatesRequest(
    IReadOnlyList<string>? ProductIds = null,
    bool Force = false);

internal sealed class CheckWingetUpdatesHandler
    : IActionHandler<CheckWingetUpdatesRequest, WingetUpdateCheckResult>
{
    private readonly WingetPackageService _service;
    public CheckWingetUpdatesHandler(WingetPackageService service) => _service = service;
    public string Module => "patchmanagement";
    public string Action => "checkWingetUpdates";
    public Task<Result<WingetUpdateCheckResult>> HandleAsync(
        CheckWingetUpdatesRequest payload,
        CancellationToken cancellationToken) =>
        _service.CheckUpdatesAsync(payload.ProductIds, payload.Force, cancellationToken);
}

public sealed record PrepareWingetUpdatesRequest(IReadOnlyList<WingetUpdateSelection> Packages);

internal sealed class PrepareWingetUpdatesHandler
    : IActionHandler<PrepareWingetUpdatesRequest, WingetUpdatePlan>
{
    private readonly WingetPackageService _service;
    public PrepareWingetUpdatesHandler(WingetPackageService service) => _service = service;
    public string Module => "patchmanagement";
    public string Action => "prepareWingetUpdates";
    public Task<Result<WingetUpdatePlan>> HandleAsync(
        PrepareWingetUpdatesRequest payload,
        CancellationToken cancellationToken) =>
        _service.PrepareUpdatesAsync(payload.Packages ?? [], cancellationToken);
}

public sealed record ApplyWingetUpdatesRequest(
    IReadOnlyList<WingetUpdateSelection> Packages,
    bool Confirmed = false);

internal sealed class ApplyWingetUpdatesHandler
    : IActionHandler<ApplyWingetUpdatesRequest, WingetUpdateOutcome>
{
    private readonly WingetPackageService _service;
    public ApplyWingetUpdatesHandler(WingetPackageService service) => _service = service;
    public string Module => "patchmanagement";
    public string Action => "applyWingetUpdates";
    public Task<Result<WingetUpdateOutcome>> HandleAsync(
        ApplyWingetUpdatesRequest payload,
        CancellationToken cancellationToken) =>
        _service.ApplyUpdatesAsync(payload.Packages ?? [], payload.Confirmed, cancellationToken);
}
