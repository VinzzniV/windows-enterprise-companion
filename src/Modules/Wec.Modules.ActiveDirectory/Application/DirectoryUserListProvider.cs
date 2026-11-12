using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Results;

namespace Wec.Modules.ActiveDirectory.Application;

internal sealed class DirectoryUserListProvider(IDirectoryUserReadProvider reader,
    DirectoryUserListCache cache, IClock clock) : IDirectoryUserListProvider
{
    public Task<Result<CachedDirectoryUserList>> ReadCachedAsync(DirectoryUserListQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Result<DirectoryUserListQuery> normalized = Normalize(query);
        return Task.FromResult(normalized.IsFailure ? Result.Failure<CachedDirectoryUserList>(normalized.Error!)
            : Result.Success(cache.Read(normalized.Value, cancellationToken)));
    }

    public Task<Result<CachedDirectoryUserList>> ReadPageAsync(DirectoryUserListQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Result<DirectoryUserListQuery> normalized = Normalize(query);
        if (normalized.IsFailure) { return Task.FromResult(Result.Failure<CachedDirectoryUserList>(normalized.Error!)); }
        query = normalized.Value;
        return cache.LoadAsync(query, async token =>
        {
            Result<DirectoryUserPage> result = await reader.GetPageAsync(new(query.Connection, query.Search, null, null,
                DirectoryUserAccountStateFilter.All, query.Page, query.PageSize, DirectoryUserSortField.SamAccountName,
                DirectoryUserSortDirection.Ascending, query.DirectoryScope), token);
            if (result.IsFailure) { return Result.Failure<DirectoryUserListPage>(result.Error!); }
            DirectoryUserPage page = result.Value;
            if (!page.DomainJoined || !string.Equals(page.DomainName, query.DirectoryScope, StringComparison.OrdinalIgnoreCase)
                || page.Users.Any(user => !string.Equals(user.DirectoryScope, query.DirectoryScope, StringComparison.OrdinalIgnoreCase)))
            {
                return Result.Failure<DirectoryUserListPage>(new(ErrorCode.DirectoryUnavailable,
                    "The connected directory naming context does not match this user list."));
            }
            return Result.Success(new DirectoryUserListPage(query.DirectoryScope, clock.UtcNow, page.Page, page.PageSize,
                page.TotalCount, page.Users.Select(user => new DirectoryUserListEntry(user.ObjectId, user.Sid, user.DirectoryScope!,
                    user.DisplayName, user.SamAccountName, user.UserPrincipalName, user.Enabled, user.Department)).ToArray()));
        }, cancellationToken);
    }

    private static Result<DirectoryUserListQuery> Normalize(DirectoryUserListQuery query)
    {
        string scope = query.DirectoryScope?.Trim().TrimEnd('.').ToLowerInvariant() ?? string.Empty;
        if (Uri.CheckHostName(scope) != UriHostNameType.Dns || scope.Length > 253 || query.Page < 1
            || query.PageSize is < 1 or > 100 || ((long)query.Page - 1) * query.PageSize > int.MaxValue
            || (query.Search?.Trim().Length ?? 0) > 100 || query.Search?.Any(char.IsControl) == true)
        {
            return Result.Failure<DirectoryUserListQuery>(new(ErrorCode.InvalidRequest,
                "Use a directory DNS scope, a valid page with 1–100 rows and at most 100 search characters."));
        }
        return Result.Success(query with { DirectoryScope = scope, Search = query.Search?.Trim() });
    }
}
