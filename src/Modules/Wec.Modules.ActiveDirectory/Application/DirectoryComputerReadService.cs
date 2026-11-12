using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Results;

namespace Wec.Modules.ActiveDirectory.Application;

internal sealed class DirectoryComputerReadService(DomainContextService domainContext, IDirectoryReader reader,
    IClock clock, IOptions<ActiveDirectoryOptions> options, DirectoryComputerSnapshotCache cache) : IDirectoryComputerReadProvider
{
    public Task<CachedDirectoryComputer?> ReadCachedAsync(DirectoryComputerIdentityQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(IsValid(query) ? cache.Read(query, cancellationToken) : null);
    }

    public Task<Result<DirectoryComputerIdentityResult>> ReadIdentityAsync(DirectoryComputerIdentityQuery query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return IsValid(query) ? cache.LoadAsync(query, token => LoadIdentityAsync(query, token), cancellationToken)
            : Task.FromResult(Result.Failure<DirectoryComputerIdentityResult>(new(ErrorCode.InvalidRequest,
                "Specify a directory DNS scope and exactly one valid computer GUID or account SID.")));
    }

    private static bool IsValid(DirectoryComputerIdentityQuery query) => !string.IsNullOrWhiteSpace(query.DirectoryScope)
        && query.DirectoryScope.Length <= 253 && Uri.CheckHostName(query.DirectoryScope.Trim().TrimEnd('.')) == UriHostNameType.Dns
        && query.ObjectId != Guid.Empty && (query.ObjectId is null) != (query.SecurityIdentifier is null)
        && (query.SecurityIdentifier is null || DirectoryIdentityValues.AccountSid(query.SecurityIdentifier) is not null);

    private async Task<Result<DirectoryComputerIdentityResult>> LoadIdentityAsync(DirectoryComputerIdentityQuery query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? sid = DirectoryIdentityValues.AccountSid(query.SecurityIdentifier);
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
