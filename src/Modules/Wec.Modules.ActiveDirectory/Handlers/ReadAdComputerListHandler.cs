using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.ActiveDirectory.Application;

namespace Wec.Modules.ActiveDirectory.Handlers;

public sealed record ReadAdComputerListRequest(string DirectoryScope, DirectoryConnectionRequest? Connection = null,
    string? Search = null, int Limit = 100, bool Refresh = false);
public sealed record ReadAdComputerListResult(string DirectoryScope, string? Search, int Limit,
    CachedDirectoryComputer? Read, DateTimeOffset? FreshUntilUtc);

internal sealed class ReadAdComputerListHandler(ComputerSearchService search, DirectoryComputerSnapshotCache cache,
    IClock clock, IOptions<ActiveDirectoryOptions> options) : IActionHandler<ReadAdComputerListRequest, ReadAdComputerListResult>
{
    public string Module => "activedirectory";
    public string Action => "readComputerList";

    public async Task<Result<ReadAdComputerListResult>> HandleAsync(ReadAdComputerListRequest payload, CancellationToken cancellationToken)
    {
        string scope = payload.DirectoryScope?.Trim().TrimEnd('.').ToLowerInvariant() ?? string.Empty;
        string? term = string.IsNullOrWhiteSpace(payload.Search) ? null : payload.Search.Trim();
        if (scope.Length > 253 || Uri.CheckHostName(scope) != UriHostNameType.Dns || payload.Limit is < 1 or > 100
            || term?.Length > 100 || term?.Any(char.IsControl) == true)
        {
            return Result.Failure<ReadAdComputerListResult>(new(ErrorCode.InvalidRequest,
                "Use a directory DNS scope, 1–100 results and at most 100 search characters."));
        }
        Result<DirectoryConnection> connection = (payload.Connection ?? new DirectoryConnectionRequest(Domain: scope)).ToConnection();
        if (connection.IsFailure) { return Result.Failure<ReadAdComputerListResult>(connection.Error!); }
        var query = new DirectoryComputerIdentityQuery(new(connection.Value.DomainOverride, connection.Value.Server, connection.Value.Credentials), scope);
        string key = DirectoryComputerSnapshotCache.DiscoveryKey(term, payload.Limit);
        CachedDirectoryComputer? cached;
        if (payload.Refresh)
        {
            Result<DirectoryComputerIdentityResult> loaded = await cache.LoadAsync(query, async token =>
            {
                Result<AdComputerSearchResult> result = await search.SearchAsync(connection.Value, term, true, token, payload.Limit, scope);
                if (result.IsFailure) { return Result.Failure<DirectoryComputerIdentityResult>(result.Error!); }
                if (!result.Value.DomainJoined || result.Value.DomainName != scope)
                {
                    return Result.Failure<DirectoryComputerIdentityResult>(new(ErrorCode.DirectoryUnavailable, "No matching directory context is available."));
                }
                return Result.Success(new DirectoryComputerIdentityResult(scope, clock.UtcNow,
                    result.Value.Computers.Select(ComputerSearchService.ToInventory).ToArray(), result.Value.Truncated));
            }, cancellationToken, key);
            cached = cache.ReadDiscovery(query, key, cancellationToken, activate: false);
            if (cached is null && loaded.IsFailure) { return Result.Failure<ReadAdComputerListResult>(loaded.Error!); }
        }
        else { cached = cache.ReadDiscovery(query, key, cancellationToken); }
        return Result.Success(new ReadAdComputerListResult(scope, term, payload.Limit, cached,
            cached?.Data?.RetrievedAtUtc + options.Value.IdentityCacheFreshFor));
    }
}
