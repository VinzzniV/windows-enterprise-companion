using Wec.Core.Contracts;
using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.UserManagement.Application;
using Wec.Modules.UserManagement.Domain;

namespace Wec.Modules.UserManagement.Handlers;

public sealed record ListUsersRequest(
    string? Search = null,
    string? BaseDistinguishedName = null,
    string? Department = null,
    DirectoryUserAccountStateFilter AccountState = DirectoryUserAccountStateFilter.All,
    int Page = 1,
    int PageSize = 25,
    DirectoryUserSortField SortField = DirectoryUserSortField.DisplayName,
    DirectoryUserSortDirection SortDirection = DirectoryUserSortDirection.Ascending,
    UserDirectoryConnectionRequest? Connection = null);

internal sealed class ListUsersHandler : IActionHandler<ListUsersRequest, UserPageResult>
{
    private readonly UserManagementService _userManagementService;

    public ListUsersHandler(UserManagementService userManagementService)
    {
        _userManagementService = userManagementService;
    }

    public string Module => "usermanagement";
    public string Action => "listUsers";

    public async Task<Result<UserPageResult>> HandleAsync(
        ListUsersRequest payload,
        CancellationToken cancellationToken)
    {
        Result<DirectoryUserReadConnection> connection =
            (payload.Connection ?? new UserDirectoryConnectionRequest()).ToConnection();
        if (connection.IsFailure)
        {
            return Result.Failure<UserPageResult>(connection.Error!);
        }

        return await _userManagementService.GetPageAsync(
            new DirectoryUserPageQuery(
                connection.Value,
                payload.Search,
                payload.BaseDistinguishedName,
                payload.Department,
                payload.AccountState,
                payload.Page,
                payload.PageSize,
                payload.SortField,
                payload.SortDirection),
            cancellationToken);
    }
}
