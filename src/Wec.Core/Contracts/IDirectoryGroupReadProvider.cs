using Wec.Core.Objects;
using Wec.Core.Results;

namespace Wec.Core.Contracts;

public sealed record DirectoryGroupIdentityQuery(DirectoryUserReadConnection Connection, string DirectoryScope,
    Guid? ObjectId = null, string? SecurityIdentifier = null, string? DistinguishedName = null);
public sealed record DirectoryGroupPageQuery(DirectoryUserReadConnection Connection, string DirectoryScope,
    string? Search = null, int Page = 1, int PageSize = 50);
public sealed record DirectoryGroupMemberQuery(DirectoryUserReadConnection Connection, string DirectoryScope,
    Guid GroupObjectId, int Page = 1, int PageSize = 50);

public sealed record DirectoryGroupRecord(Guid? ObjectId, string? SecurityIdentifier, string DirectoryScope,
    string Name, string? SamAccountName, string DistinguishedName, string? Description,
    bool? SecurityEnabled, string? GroupScope);
public sealed record DirectoryGroupMember(Guid? ObjectId, string? SecurityIdentifier, ObjectKind? Kind,
    string? ObjectClass, string DisplayName, string? SamAccountName, string? UserPrincipalName,
    string DistinguishedName);
public sealed record DirectoryGroupIdentityResult(string DirectoryScope, DateTimeOffset RetrievedAtUtc,
    IReadOnlyList<DirectoryGroupRecord> Groups, bool Truncated);
public sealed record DirectoryGroupPage(string DirectoryScope, DateTimeOffset RetrievedAtUtc, int Page, int PageSize,
    int TotalCount, IReadOnlyList<DirectoryGroupRecord> Groups);
public sealed record DirectoryGroupMemberPage(string DirectoryScope, Guid GroupObjectId, DateTimeOffset RetrievedAtUtc,
    int Page, int PageSize, int TotalCount, IReadOnlyList<DirectoryGroupMember> Members, string CoverageExplanation);

public sealed record DirectoryGroupReadState(DateTimeOffset? RetrievedAtUtc, DateTimeOffset? LastAttemptAtUtc,
    Error? LastAttemptError, long SessionRevision, long Revision, DateTimeOffset? RetainedUntilUtc,
    DateTimeOffset? FreshUntilUtc, bool Stale);
public sealed record CachedDirectoryGroupIdentity(DirectoryGroupReadState State, DirectoryGroupIdentityResult? Data);
public sealed record CachedDirectoryGroupMembers(DirectoryGroupReadState State, DirectoryGroupMemberPage? Data);
public sealed record CachedDirectoryGroupPage(DirectoryGroupReadState State, DirectoryGroupPage? Data);

public interface IDirectoryGroupReadProvider
{
    Task<CachedDirectoryGroupIdentity> ReadCachedIdentityAsync(DirectoryGroupIdentityQuery query, CancellationToken cancellationToken);
    Task<CachedDirectoryGroupMembers> ReadCachedMembersAsync(DirectoryGroupMemberQuery query, CancellationToken cancellationToken);
    Task<CachedDirectoryGroupPage> ReadCachedPageAsync(DirectoryGroupPageQuery query, CancellationToken cancellationToken);
    Task<Result<DirectoryGroupIdentityResult>> ReadIdentityAsync(DirectoryGroupIdentityQuery query, CancellationToken cancellationToken);
    Task<Result<DirectoryGroupPage>> ReadPageAsync(DirectoryGroupPageQuery query, CancellationToken cancellationToken);
    Task<Result<DirectoryGroupMemberPage>> ReadMembersAsync(DirectoryGroupMemberQuery query, CancellationToken cancellationToken);
}
