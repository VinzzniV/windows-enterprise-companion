using Wec.Core.Contracts;
using Wec.Core.Results;
using Wec.Modules.UserManagement.Domain;

namespace Wec.Modules.UserManagement.Application;

internal sealed class UserManagementService
{
    private readonly IDirectoryUserReadProvider _directoryUsers;
    private readonly IUserDeviceRelationshipProvider _deviceRelationships;

    public UserManagementService(
        IDirectoryUserReadProvider directoryUsers,
        IUserDeviceRelationshipProvider deviceRelationships)
    {
        _directoryUsers = directoryUsers;
        _deviceRelationships = deviceRelationships;
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
        UserDeviceProfile devices = await GetDeviceProfileAsync(user.Sid, cancellationToken);
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
                user.PrivilegedAccess.DirectMemberships),
            devices));
    }

    private async Task<UserDeviceProfile> GetDeviceProfileAsync(
        string? directorySid,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(directorySid))
        {
            return new UserDeviceProfile(
                UserDeviceEvidenceCoverage.NotEvaluated,
                "The directory user has no SID, so device evidence cannot be matched safely.",
                EmptyDeviceCoverage(),
                []);
        }

        UserDeviceRelationshipSnapshot snapshot = await _deviceRelationships.GetForDirectorySidAsync(
            directorySid,
            cancellationToken);
        UserDeviceEvidenceCoverage coverage = ToCoverage(snapshot.Coverage);
        return new UserDeviceProfile(
            coverage,
            CoverageExplanation(coverage, snapshot.Coverage),
            snapshot.Coverage,
            snapshot.Devices);
    }

    private static UserDeviceEvidenceCoverage ToCoverage(UserDeviceRelationshipCoverage coverage)
    {
        if (coverage.StoredDeviceCount > 0
            && coverage.NotCapturedDeviceCount == coverage.StoredDeviceCount
            && coverage.UnavailableDeviceCount == 0)
        {
            return UserDeviceEvidenceCoverage.NotCaptured;
        }

        if (coverage.NotCapturedDeviceCount > 0
            || coverage.UnavailableDeviceCount > 0
            || coverage.TruncatedDeviceCount > 0)
        {
            return UserDeviceEvidenceCoverage.Partial;
        }

        return UserDeviceEvidenceCoverage.Available;
    }

    private static string CoverageExplanation(
        UserDeviceEvidenceCoverage coverage,
        UserDeviceRelationshipCoverage sourceCoverage) => coverage switch
    {
        UserDeviceEvidenceCoverage.Available when sourceCoverage.StoredDeviceCount == 0 =>
            "No stored Inventory devices are available for relationship evaluation.",
        UserDeviceEvidenceCoverage.Available =>
            "All stored Inventory devices contain evaluated user relationship evidence.",
        UserDeviceEvidenceCoverage.NotCaptured =>
            "Stored Inventory snapshots predate user relationship evidence; run an explicit Inventory scan to evaluate them.",
        _ =>
            "Some stored devices have missing, unavailable or truncated Inventory user evidence.",
    };

    private static UserDeviceRelationshipCoverage EmptyDeviceCoverage() => new(0, 0, 0, 0, 0);

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
