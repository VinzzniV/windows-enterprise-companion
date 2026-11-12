using Wec.Core.Results;

namespace Wec.Core.Contracts;

public sealed record DirectoryUserListQuery(DirectoryUserReadConnection Connection, string DirectoryScope,
    string? Search = null, int Page = 1, int PageSize = 100);

public sealed record DirectoryUserListEntry(Guid ObjectId, string? SecurityIdentifier, string DirectoryScope,
    string DisplayName, string? SamAccountName, string? UserPrincipalName, bool? Enabled, string? Department);

public sealed record DirectoryUserListPage(string DirectoryScope, DateTimeOffset RetrievedAtUtc,
    int Page, int PageSize, int TotalCount, IReadOnlyList<DirectoryUserListEntry> Users);

public sealed record CachedDirectoryUserList(DirectoryUserListPage? Data, DateTimeOffset? LastAttemptAtUtc,
    Error? LastAttemptError, long SessionRevision, long Revision, DateTimeOffset? RetainedUntilUtc,
    DateTimeOffset? FreshUntilUtc, bool Stale);

public interface IDirectoryUserListProvider
{
    Task<Result<CachedDirectoryUserList>> ReadCachedAsync(DirectoryUserListQuery query, CancellationToken cancellationToken);
    Task<Result<CachedDirectoryUserList>> ReadPageAsync(DirectoryUserListQuery query, CancellationToken cancellationToken);
}
