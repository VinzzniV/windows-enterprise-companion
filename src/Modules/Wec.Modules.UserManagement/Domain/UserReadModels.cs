using Wec.Core.Contracts;

namespace Wec.Modules.UserManagement.Domain;

public sealed record UserSummary(
    Guid ObjectId,
    string DisplayName,
    string? SamAccountName,
    string? UserPrincipalName,
    string? EmployeeId,
    string? Department,
    string? Title,
    string OrganizationalUnitPath,
    bool? Enabled,
    DateTimeOffset? ReplicatedLastLogonAtUtc);

public sealed record UserPageResult(
    bool DomainJoined,
    string? DomainName,
    string? BaseDistinguishedName,
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<UserSummary> Users);

public sealed record UserIdentityProfile(
    Guid ObjectId,
    string? Sid,
    string DisplayName,
    string? SamAccountName,
    string? UserPrincipalName,
    string? Mail,
    string? EmployeeId,
    string? Department,
    string? Title,
    string? ManagerDistinguishedName,
    string DistinguishedName,
    string OrganizationalUnitPath);

public sealed record UserLifecycleProfile(
    bool? Enabled,
    DateTimeOffset? CreatedAtUtc,
    DateTimeOffset? AccountExpiresAtUtc,
    DateTimeOffset? ReplicatedLastLogonAtUtc,
    DateTimeOffset? PasswordLastSetAtUtc,
    DateTimeOffset? PasswordExpiresAtUtc,
    bool? PasswordNeverExpires);

public sealed record UserAccessProfile(
    IReadOnlyList<DirectoryUserGroup> DirectGroups,
    DirectoryUserAccessCoverage PrivilegedCoverage,
    string PrivilegedCoverageExplanation,
    IReadOnlyList<DirectoryUserGroup> DirectPrivilegedGroups);

public enum UserDeviceEvidenceCoverage
{
    NotEvaluated = 0,
    Available,
    Partial,
    NotCaptured,
}

public sealed record UserDeviceProfile(
    UserDeviceEvidenceCoverage Coverage,
    string Explanation,
    UserDeviceRelationshipCoverage SourceCoverage,
    IReadOnlyList<UserLinkedDeviceEvidence> LinkedDevices);

public sealed record UserProfileResult(
    UserIdentityProfile Identity,
    UserLifecycleProfile Lifecycle,
    UserAccessProfile Access,
    UserDeviceProfile Devices);
