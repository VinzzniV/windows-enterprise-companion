using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Messaging;
using Wec.Core.Objects;
using Wec.Core.Results;

namespace Wec.Modules.Clients.Handlers;

public sealed record StoredObjectListsRequest(string? Search = null);
public sealed record StoredObjectListRead(StoredDeviceListSource Source, string Revision, int? TotalRecords,
    IReadOnlyList<StoredDeviceAddressRow> Records, Error? Error);
public sealed record StoredObjectLists(WecWorkspaceIdentity Workspace, int MaximumRecords,
    DateTimeOffset RetrievedAtUtc, string? Search, IReadOnlyList<StoredObjectListRead> Reads);

internal sealed class StoredObjectListsHandler(IEnumerable<IStoredDeviceListProvider> sources,
    WecWorkspaceIdentity workspace, IOptions<ObjectWorkingSetOptions> options, IClock clock)
    : IActionHandler<StoredObjectListsRequest, StoredObjectLists>
{
    public string Module => "clients";
    public string Action => "getStoredObjectLists";
    public async Task<Result<StoredObjectLists>> HandleAsync(StoredObjectListsRequest request, CancellationToken cancellationToken)
    {
        string? search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();
        if (search is not null && (search.Length > 100 || search.Any(char.IsControl)))
        {
            return Result.Failure<StoredObjectLists>(new(ErrorCode.InvalidRequest, "Use at most 100 search characters."));
        }
        var reads = new List<StoredObjectListRead>();
        foreach (IStoredDeviceListProvider source in sources.OrderBy(provider => provider.Source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Result<StoredDeviceAddressPage> page = await source.ReadAsync(options.Value.MaximumRecords, search, cancellationToken);
            IReadOnlyList<StoredDeviceAddressRow> records = page.IsSuccess ? page.Value.Records : [];
            int? total = page.IsSuccess ? page.Value.TotalRecords : null;
            string revision = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { total, records, page.Error })));
            reads.Add(new(source.Source, revision, total, records, page.Error));
        }
        return Result.Success(new StoredObjectLists(workspace, options.Value.MaximumRecords, clock.UtcNow, search, reads));
    }
}
