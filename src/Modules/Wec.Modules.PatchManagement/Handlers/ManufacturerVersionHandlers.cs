using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.PatchManagement.Application;
using Wec.Modules.PatchManagement.Persistence;

namespace Wec.Modules.PatchManagement.Handlers;

public sealed record VersionSourcesResult(IReadOnlyList<ProductVersionSource> Sources);
public sealed record ListVersionSourcesRequest;
public sealed record SaveVersionSourceRequest(
    string ProductId,
    string SourceUrl,
    string VersionPattern,
    bool Enabled = true);
public sealed record DeleteVersionSourceRequest(string ProductId);
public sealed record CheckVendorVersionsRequest(IReadOnlyList<string>? ProductIds = null);
public sealed record VersionCheckResult(IReadOnlyList<VersionCheckOutcome> Outcomes);

internal sealed class ListVersionSourcesHandler
    : IActionHandler<ListVersionSourcesRequest, VersionSourcesResult>
{
    private readonly IProductVersionSourceRepository _repository;

    public ListVersionSourcesHandler(IProductVersionSourceRepository repository) => _repository = repository;
    public string Module => "patchmanagement";
    public string Action => "listVersionSources";

    public async Task<Result<VersionSourcesResult>> HandleAsync(
        ListVersionSourcesRequest payload,
        CancellationToken cancellationToken) =>
        Result.Success(new VersionSourcesResult(await _repository.ListAsync(cancellationToken)));
}

internal sealed class SaveVersionSourceHandler
    : IActionHandler<SaveVersionSourceRequest, VersionSourcesResult>
{
    private readonly IProductVersionSourceRepository _repository;

    public SaveVersionSourceHandler(IProductVersionSourceRepository repository) => _repository = repository;
    public string Module => "patchmanagement";
    public string Action => "saveVersionSource";

    public async Task<Result<VersionSourcesResult>> HandleAsync(
        SaveVersionSourceRequest payload,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payload.ProductId)
            || !Uri.TryCreate(payload.SourceUrl, UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(payload.VersionPattern))
        {
            return Result.Failure<VersionSourcesResult>(new Error(
                ErrorCode.InvalidRequest,
                "Product id, an HTTPS source URL and a version pattern are required."));
        }

        ProductVersionSource? existing = (await _repository.ListAsync(cancellationToken))
            .FirstOrDefault(source => string.Equals(source.ProductId, payload.ProductId, StringComparison.OrdinalIgnoreCase));
        await _repository.UpsertAsync(new ProductVersionSource(
            existing?.ProductId ?? payload.ProductId.Trim(),
            uri.ToString(),
            payload.VersionPattern,
            payload.Enabled,
            existing?.LatestVersion,
            existing?.LastCheckedUtc,
            existing?.CheckStatus ?? "NOT_CHECKED",
            existing?.LastError), cancellationToken);
        return Result.Success(new VersionSourcesResult(await _repository.ListAsync(cancellationToken)));
    }
}

internal sealed class DeleteVersionSourceHandler
    : IActionHandler<DeleteVersionSourceRequest, VersionSourcesResult>
{
    private readonly IProductVersionSourceRepository _repository;

    public DeleteVersionSourceHandler(IProductVersionSourceRepository repository) => _repository = repository;
    public string Module => "patchmanagement";
    public string Action => "deleteVersionSource";

    public async Task<Result<VersionSourcesResult>> HandleAsync(
        DeleteVersionSourceRequest payload,
        CancellationToken cancellationToken)
    {
        await _repository.DeleteAsync(payload.ProductId, cancellationToken);
        return Result.Success(new VersionSourcesResult(await _repository.ListAsync(cancellationToken)));
    }
}

internal sealed class CheckVendorVersionsHandler
    : IActionHandler<CheckVendorVersionsRequest, VersionCheckResult>
{
    private readonly ManufacturerVersionService _service;

    public CheckVendorVersionsHandler(ManufacturerVersionService service) => _service = service;
    public string Module => "patchmanagement";
    public string Action => "checkVendorVersions";

    public async Task<Result<VersionCheckResult>> HandleAsync(
        CheckVendorVersionsRequest payload,
        CancellationToken cancellationToken) =>
        Result.Success(new VersionCheckResult(
            await _service.CheckAsync(payload.ProductIds, force: true, cancellationToken)));
}
