using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.Inventory.Persistence;

namespace Wec.Modules.Inventory.Handlers;

public sealed record ListInventoryHostsRequest;

public sealed record ListInventoryHostsResult(IReadOnlyList<StoredInventoryHost> Hosts);

/// <summary>Stored snapshots per host — lets the UI restore scanned computers across page switches.</summary>
internal sealed class ListInventoryHostsHandler : IActionHandler<ListInventoryHostsRequest, ListInventoryHostsResult>
{
    private readonly IHardwareSnapshotRepository _repository;

    public ListInventoryHostsHandler(IHardwareSnapshotRepository repository)
    {
        _repository = repository;
    }

    public string Module => "inventory";

    public string Action => "listHosts";

    public async Task<Result<ListInventoryHostsResult>> HandleAsync(
        ListInventoryHostsRequest payload,
        CancellationToken cancellationToken) =>
        Result.Success(new ListInventoryHostsResult(await _repository.ListHostsAsync(cancellationToken)));
}

public sealed record DeleteHostSnapshotRequest(string Host);

public sealed record DeleteHostSnapshotResult(string Host);

internal sealed class DeleteHostSnapshotHandler : IActionHandler<DeleteHostSnapshotRequest, DeleteHostSnapshotResult>
{
    private readonly IHardwareSnapshotRepository _repository;

    public DeleteHostSnapshotHandler(IHardwareSnapshotRepository repository)
    {
        _repository = repository;
    }

    public string Module => "inventory";

    public string Action => "deleteHostSnapshot";

    public async Task<Result<DeleteHostSnapshotResult>> HandleAsync(
        DeleteHostSnapshotRequest payload,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payload.Host))
        {
            return Result.Failure<DeleteHostSnapshotResult>(new Error(
                ErrorCode.InvalidRequest, "Host must not be empty."));
        }

        string hostKey = payload.Host.Trim().ToUpperInvariant();
        await _repository.DeleteAsync(hostKey, cancellationToken);
        return Result.Success(new DeleteHostSnapshotResult(hostKey));
    }
}
