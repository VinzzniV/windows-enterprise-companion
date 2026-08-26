using Wec.Core.Contracts;
using Wec.Core.Results;
using Wec.Modules.UserManagement.Domain;

namespace Wec.Modules.UserManagement.Application;

internal sealed class UserManagementService
{
    private readonly IDirectoryUserReadProvider _directoryUsers;

    public UserManagementService(IDirectoryUserReadProvider directoryUsers)
    {
        _directoryUsers = directoryUsers;
    }

    public async Task<Result<UserPageResult>> GetPageAsync(
        DirectoryUserPageQuery query,
        CancellationToken cancellationToken)
    {
        Result<DirectoryUserPage> page = await _directoryUsers.GetPageAsync(query, cancellationToken);
        return page.IsFailure
            ? Result.Failure<UserPageResult>(page.Error!)
            : Result.Success(new UserPageResult(
                page.Value.DomainJoined,
                page.Value.DomainName,
                page.Value.BaseDistinguishedName,
                page.Value.Page,
                page.Value.PageSize,
                page.Value.TotalCount,
                [.. page.Value.Users.Select(ToSummary)]));
    }

    public async Task<Result<UserProfileResult>> GetProfileAsync(
        DirectoryUserIdentityQuery query,
        CancellationToken cancellationToken)
    {
        Result<DirectoryUserRecord?> result = await _directoryUsers.GetByIdAsync(query, cancellationToken);
        if (result.IsFailure)
        {
            return Result.Failure<UserProfileResult>(result.Error!);
        }

        if (result.Value is null)
        {
            return Result.Failure<UserProfileResult>(new Error(
                ErrorCode.NotFound,
                "The requested directory user was not found."));
        }

        DirectoryUserRecord user = result.Value;
        return Result.Success(new UserProfileResult(
            new UserIdentityProfile(
                user.ObjectId,
                user.Sid,
                user.DisplayName,
                user.SamAccountName,
                user.UserPrincipalName,
                user.Mail,
                user.EmployeeId,
                user.Department,
                user.Title,
                user.ManagerDistinguishedName,
                user.DistinguishedName,
                user.OrganizationalUnitPath),
            new UserLifecycleProfile(
                user.Enabled,
                user.CreatedAtUtc,
                user.AccountExpiresAtUtc,
                user.ReplicatedLastLogonAtUtc,
                user.PasswordLastSetAtUtc,
                user.PasswordExpiresAtUtc,
                user.PasswordNeverExpires),
            new UserAccessProfile(
                user.DirectGroups,
                user.PrivilegedAccess.Coverage,
                user.PrivilegedAccess.Explanation,
                user.PrivilegedAccess.DirectMemberships)));
    }

    private static UserSummary ToSummary(DirectoryUserRecord user) => new(
        user.ObjectId,
        user.DisplayName,
        user.SamAccountName,
        user.UserPrincipalName,
        user.EmployeeId,
        user.Department,
        user.Title,
        user.OrganizationalUnitPath,
        user.Enabled,
        user.ReplicatedLastLogonAtUtc);
}
