using Wec.Core.Contracts;
using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.UserManagement.Application;
using Wec.Modules.UserManagement.Domain;

namespace Wec.Modules.UserManagement.Handlers;

public sealed record GetUserProfileRequest(
    Guid ObjectId,
    UserDirectoryConnectionRequest? Connection = null);

internal sealed class GetUserProfileHandler : IActionHandler<GetUserProfileRequest, UserProfileResult>
{
    private readonly UserManagementService _userManagementService;

    public GetUserProfileHandler(UserManagementService userManagementService)
    {
        _userManagementService = userManagementService;
    }

    public string Module => "usermanagement";
    public string Action => "getUserProfile";

    public async Task<Result<UserProfileResult>> HandleAsync(
        GetUserProfileRequest payload,
        CancellationToken cancellationToken)
    {
        Result<DirectoryUserReadConnection> connection =
            (payload.Connection ?? new UserDirectoryConnectionRequest()).ToConnection();
        if (connection.IsFailure)
        {
            return Result.Failure<UserProfileResult>(connection.Error!);
        }

        return await _userManagementService.GetProfileAsync(
            new DirectoryUserIdentityQuery(connection.Value, payload.ObjectId),
            cancellationToken);
    }
}
