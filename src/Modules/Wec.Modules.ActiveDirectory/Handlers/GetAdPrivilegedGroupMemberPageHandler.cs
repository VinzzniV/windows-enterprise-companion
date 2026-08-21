using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.ActiveDirectory.Application;
using Wec.Modules.ActiveDirectory.Domain;

namespace Wec.Modules.ActiveDirectory.Handlers;

public sealed record GetAdPrivilegedGroupMemberPageRequest(
    string GroupDistinguishedName,
    string? Query = null,
    int Page = 1,
    int PageSize = 50,
    DirectoryConnectionRequest? Connection = null);

internal sealed class GetAdPrivilegedGroupMemberPageHandler
    : IActionHandler<GetAdPrivilegedGroupMemberPageRequest, AdPrivilegedGroupMemberPage>
{
    private readonly DirectoryHygieneService _directoryHygieneService;

    public GetAdPrivilegedGroupMemberPageHandler(DirectoryHygieneService directoryHygieneService)
    {
        _directoryHygieneService = directoryHygieneService;
    }

    public string Module => "activedirectory";

    public string Action => "getPrivilegedGroupMemberPage";

    public Task<Result<AdPrivilegedGroupMemberPage>> HandleAsync(
        GetAdPrivilegedGroupMemberPageRequest payload,
        CancellationToken cancellationToken)
    {
        Result<DirectoryConnection> connection =
            (payload.Connection ?? new DirectoryConnectionRequest()).ToConnection();
        if (connection.IsFailure)
        {
            return Task.FromResult(Result.Failure<AdPrivilegedGroupMemberPage>(connection.Error!));
        }

        return _directoryHygieneService.GetPrivilegedGroupMemberPageAsync(
            connection.Value,
            payload.GroupDistinguishedName,
            payload.Query,
            payload.Page,
            payload.PageSize,
            cancellationToken);
    }
}
