using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Results;

namespace Wec.Modules.ActiveDirectory.Application;

internal sealed class DirectoryComputerReadService(DomainContextService domainContext, IDirectoryReader reader,
    IClock clock, IOptions<ActiveDirectoryOptions> options, DirectoryComputerSnapshotCache cache) : IDirectoryComputerReadProvider
{
    public Task<CachedDirectoryComputer?> ReadCachedAsync(DirectoryComputerIdentityQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(cache.Read(query, cancellationToken));

    public Task<Result<DirectoryComputerIdentityResult>> ReadIdentityAsync(DirectoryComputerIdentityQuery query,
        CancellationToken cancellationToken) => cache.LoadAsync(query, token => LoadIdentityAsync(query, token), cancellationToken);

    private async Task<Result<DirectoryComputerIdentityResult>> LoadIdentityAsync(DirectoryComputerIdentityQuery query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? sid = DirectoryIdentityValues.AccountSid(query.SecurityIdentifier);
        if (string.IsNullOrWhiteSpace(query.DirectoryScope) || query.DirectoryScope.Length > 253
            || query.ObjectId == Guid.Empty || (query.ObjectId is null) == (query.SecurityIdentifier is null)
            || query.SecurityIdentifier is not null && sid is null)
        {
            return Result.Failure<DirectoryComputerIdentityResult>(new(ErrorCode.InvalidRequest,
                "Specify a directory scope and exactly one valid computer GUID or account SID."));
        }
        var connection = new DirectoryConnection(query.Connection.Domain, query.Connection.Server, query.Connection.Credentials);
        Result<DomainContext> context = await domainContext.GetContextAsync(connection, cancellationToken);
        if (context.IsFailure) { return Result.Failure<DirectoryComputerIdentityResult>(context.Error!); }
        string? scope = context.Value.DefaultNamingContext is { } namingContext
            ? DirectoryIdentityValues.DirectoryScope(namingContext) : null;
        if (scope is null || !string.Equals(scope, query.DirectoryScope.Trim().TrimEnd('.'), StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<DirectoryComputerIdentityResult>(new(ErrorCode.DirectoryUnavailable,
                "The connected directory naming context does not match this computer reference. Select the matching data source."));
        }
        Result<BoundedDirectorySearchResult> entries = await reader.SearchBoundedAsync(new DirectorySearchQuery(
            context.Value.DomainName!, context.Value.DefaultNamingContext!,
            query.ObjectId is { } objectId ? AdFilters.ComputerByObjectGuid(objectId) : AdFilters.ComputerBySid(sid!),
            ComputerSearchService.Attributes, DirectorySearchScope.Subtree, options.Value.PageSize,
            options.Value.SearchTimeout, connection.Server, connection.Credentials), 2, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (entries.IsFailure) { return Result.Failure<DirectoryComputerIdentityResult>(entries.Error!); }
        AdComputerInventoryItem[] computers = entries.Value.Entries.Select(entry => ComputerSearchService.MapIdentity(entry, scope)).ToArray();
        if (computers.Any(computer => query.ObjectId is { } id ? computer.ObjectId != id
            : !string.Equals(computer.SecurityIdentifier, sid, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Failure<DirectoryComputerIdentityResult>(new(ErrorCode.DirectoryUnavailable,
                "The directory response did not include the requested identity evidence."));
        }
        return Result.Success(new DirectoryComputerIdentityResult(scope, clock.UtcNow, computers,
            entries.Value.TotalCount > computers.Length));
    }
}
