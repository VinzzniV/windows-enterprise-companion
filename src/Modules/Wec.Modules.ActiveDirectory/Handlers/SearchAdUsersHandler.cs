using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.ActiveDirectory.Application;

namespace Wec.Modules.ActiveDirectory.Handlers;

public sealed record SearchAdUsersRequest(
    string? BaseDistinguishedName = null,
    bool IncludeDisabled = true,
    DirectoryConnectionRequest? Connection = null);

/// <summary>
/// User listing scoped to an OU (or the whole domain when no base DN is
/// given). Used by the Employee Lifecycle feature to show which accounts —
/// and which group memberships — actually exist in a department OU.
/// </summary>
internal sealed class SearchAdUsersHandler
    : IActionHandler<SearchAdUsersRequest, AdUserSearchResult>
{
    private readonly UserSearchService _userSearchService;

    public SearchAdUsersHandler(UserSearchService userSearchService)
    {
        _userSearchService = userSearchService;
    }

    public string Module => "activedirectory";
    public string Action => "searchUsers";

    public async Task<Result<AdUserSearchResult>> HandleAsync(
        SearchAdUsersRequest payload,
        CancellationToken cancellationToken)
    {
        Result<DirectoryConnection> connection =
            (payload.Connection ?? new DirectoryConnectionRequest()).ToConnection();
        if (connection.IsFailure)
        {
            return Result.Failure<AdUserSearchResult>(connection.Error!);
        }

        return await _userSearchService.SearchAsync(
            connection.Value, payload.BaseDistinguishedName, payload.IncludeDisabled, cancellationToken);
    }
}
