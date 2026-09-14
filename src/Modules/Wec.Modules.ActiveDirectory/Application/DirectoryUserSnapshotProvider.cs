using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Results;

namespace Wec.Modules.ActiveDirectory.Application;

internal sealed class DirectoryUserSnapshotProvider(IDirectoryUserReadProvider reader,
    DirectoryUserSnapshotCache cache, IClock clock) : IDirectoryUserSnapshotProvider
{
    public Task<CachedDirectoryUser?> ReadCachedAsync(DirectoryUserLookup query, CancellationToken cancellationToken)
    {
        Result<DirectoryUserLookup> normalized = Normalize(query);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(normalized.IsFailure ? null : cache.Read(normalized.Value, cancellationToken));
    }

    public Task<Result<DirectoryUserIdentityResult>> ReadIdentityAsync(DirectoryUserLookup query, CancellationToken cancellationToken)
    {
        Result<DirectoryUserLookup> normalized = Normalize(query);
        if (normalized.IsFailure) { return Task.FromResult(Result.Failure<DirectoryUserIdentityResult>(normalized.Error!)); }
        query = normalized.Value;
        return cache.LoadAsync(query, async token =>
        {
            Result<DirectoryUserRecord?> result = query.ObjectId is { } id
                ? await reader.GetByIdAsync(new(query.Connection, id, query.DirectoryScope), token)
                : await reader.GetBySidAsync(new(query.Connection, query.SecurityIdentifier!, query.DirectoryScope), token);
            return result.IsFailure ? Result.Failure<DirectoryUserIdentityResult>(result.Error!)
                : Result.Success(new DirectoryUserIdentityResult(query.DirectoryScope, clock.UtcNow, result.Value));
        }, cancellationToken);
    }

    private static Result<DirectoryUserLookup> Normalize(DirectoryUserLookup query)
    {
        string scope = query.DirectoryScope.Trim().TrimEnd('.').ToLowerInvariant();
        string? sid = query.SecurityIdentifier is null ? null : DirectoryIdentityValues.AccountSid(query.SecurityIdentifier);
        if (Uri.CheckHostName(scope) != UriHostNameType.Dns || scope.Length > 253 || query.ObjectId == Guid.Empty
            || (query.ObjectId is null) == (query.SecurityIdentifier is null) || query.SecurityIdentifier is not null && sid is null)
        {
            return Result.Failure<DirectoryUserLookup>(new(ErrorCode.InvalidRequest, "A directory DNS scope and exactly one valid user GUID or account SID are required."));
        }
        return Result.Success(query with { DirectoryScope = scope, SecurityIdentifier = sid });
    }
}
