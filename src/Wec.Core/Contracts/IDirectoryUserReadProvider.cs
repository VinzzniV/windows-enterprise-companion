using Wec.Core.Results;
using Wec.Core.Targets;

namespace Wec.Core.Contracts;

public enum DirectoryUserAccountStateFilter
{
    All = 0,
    Enabled,
    Disabled,
}

public enum DirectoryUserSortField
{
    DisplayName = 0,
    SamAccountName,
    Department,
    CreatedAt,
    LastLogon,
}

public enum DirectoryUserSortDirection
{
    Ascending = 0,
    Descending,
}

public sealed record DirectoryUserReadConnection(
    string? Domain,
    string? Server,
    ScanCredentials Credentials);

public sealed record DirectoryUserPageQuery(
    DirectoryUserReadConnection Connection,
    string? Search,
    string? BaseDistinguishedName,
    string? Department,
    DirectoryUserAccountStateFilter AccountState,
    int Page,
    int PageSize,
    DirectoryUserSortField SortField,
    DirectoryUserSortDirection SortDirection);

public sealed record DirectoryUserIdentityQuery(
    DirectoryUserReadConnection Connection,
    Guid ObjectId,
    string? DirectoryScope = null);

public sealed record DirectoryUserSidQuery(
    DirectoryUserReadConnection Connection,
    string SecurityIdentifier,
    string? DirectoryScope = null);

public sealed record DirectoryUserGroup(
    string DistinguishedName,
    string Name);

public enum DirectoryUserAccessCoverage
{
    NotEvaluated = 0,
    Available,
    Unavailable,
}

public sealed record DirectoryUserPrivilegedAccess(
    DirectoryUserAccessCoverage Coverage,
    string Explanation,
    IReadOnlyList<DirectoryUserGroup> DirectMemberships);

/// <summary>
/// Allowlisted AD identity and lifecycle evidence. Mutable names are display
/// fields; <see cref="ObjectId"/> is the stable directory identity (ADR 0019).
/// </summary>
public sealed record DirectoryUserRecord(
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
    string OrganizationalUnitPath,
    bool? Enabled,
    DateTimeOffset? CreatedAtUtc,
    DateTimeOffset? AccountExpiresAtUtc,
    DateTimeOffset? ReplicatedLastLogonAtUtc,
    DateTimeOffset? PasswordLastSetAtUtc,
    DateTimeOffset? PasswordExpiresAtUtc,
    bool? PasswordNeverExpires,
    IReadOnlyList<DirectoryUserGroup> DirectGroups,
    DirectoryUserPrivilegedAccess PrivilegedAccess,
    string? DirectoryScope = null);

public sealed record DirectoryUserPage(
    bool DomainJoined,
    string? DomainName,
    string? BaseDistinguishedName,
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<DirectoryUserRecord> Users);

/// <summary>
/// Concrete read-only AD user projection consumed by User Management. It
/// deliberately exposes no directory write operation and no generic query.
/// </summary>
public interface IDirectoryUserReadProvider
{
    Task<Result<DirectoryUserPage>> GetPageAsync(
        DirectoryUserPageQuery query,
        CancellationToken cancellationToken);

    Task<Result<DirectoryUserRecord?>> GetByIdAsync(
        DirectoryUserIdentityQuery query,
        CancellationToken cancellationToken);

    Task<Result<DirectoryUserRecord?>> GetBySidAsync(
        DirectoryUserSidQuery query,
        CancellationToken cancellationToken);
}
