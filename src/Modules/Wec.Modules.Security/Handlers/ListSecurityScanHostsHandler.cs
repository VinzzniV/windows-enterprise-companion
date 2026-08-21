using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.Security.Persistence;

namespace Wec.Modules.Security.Handlers;

public sealed record ListSecurityScanHostsRequest;

public sealed record ListSecurityScanHostsResult(IReadOnlyList<StoredSecurityScanHost> Hosts);

/// <summary>Latest persisted scan stamp per host for read-only availability views.</summary>
internal sealed class ListSecurityScanHostsHandler
    : IActionHandler<ListSecurityScanHostsRequest, ListSecurityScanHostsResult>
{
    private readonly ISecurityScanRepository _repository;

    public ListSecurityScanHostsHandler(ISecurityScanRepository repository)
    {
        _repository = repository;
    }

    public string Module => "security";

    public string Action => "listHosts";

    public async Task<Result<ListSecurityScanHostsResult>> HandleAsync(
        ListSecurityScanHostsRequest payload,
        CancellationToken cancellationToken) =>
        Result.Success(new ListSecurityScanHostsResult(
            await _repository.ListHostsAsync(cancellationToken)));
}
