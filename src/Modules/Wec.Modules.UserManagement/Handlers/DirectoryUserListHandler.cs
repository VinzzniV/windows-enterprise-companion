using Wec.Core.Contracts;
using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.UserManagement.Application;

namespace Wec.Modules.UserManagement.Handlers;

public sealed record DirectoryUserListRequest(string DirectoryScope, UserDirectoryConnectionRequest? Connection = null,
    string? Search = null, int Page = 1, int PageSize = 100, bool Refresh = false);

internal sealed class DirectoryUserListHandler(IDirectoryUserListProvider source) : IActionHandler<DirectoryUserListRequest, CachedDirectoryUserList>
{
    public string Module => "usermanagement";
    public string Action => "readDirectoryPage";
    public Task<Result<CachedDirectoryUserList>> HandleAsync(DirectoryUserListRequest request, CancellationToken cancellationToken)
    {
        Result<DirectoryUserReadConnection> connection = (request.Connection ?? new()).ToConnection();
        if (connection.IsFailure) { return Task.FromResult(Result.Failure<CachedDirectoryUserList>(connection.Error!)); }
        var query = new DirectoryUserListQuery(connection.Value, request.DirectoryScope, request.Search, request.Page, request.PageSize);
        return request.Refresh ? source.ReadPageAsync(query, cancellationToken) : source.ReadCachedAsync(query, cancellationToken);
    }
}
