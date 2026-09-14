using Wec.Core.Contracts;
using Wec.Core.Microsoft365;
using Wec.Core.Objects;
using Wec.Core.Results;

namespace Wec.Modules.GroupManagement;

public enum GroupProfileRead { Cached, DirectoryIdentity, DirectoryMembers }
public sealed record GroupProfileRequest(ObjectReference Reference, DirectoryInventoryConnection? Connection = null,
    GroupProfileRead Read = GroupProfileRead.Cached, int MemberPage = 1, int MemberPageSize = 50);
public sealed record GroupProfileResult(ObjectReference Reference, string Title, IdentityEvidence Identity, string Explanation,
    CachedDirectoryGroupIdentity? Directory, CachedDirectoryGroupMembers? DirectoryMembers,
    Microsoft365GroupContext? Cloud, IReadOnlyList<ObjectRelationship> Relationships, IReadOnlyList<Error> SourceErrors);

public sealed record ResolveGroupRequest(string DirectoryScope, DirectoryInventoryConnection? Connection = null,
    string? DistinguishedName = null, string? SecurityIdentifier = null);
public sealed record DirectoryGroupPageRequest(string DirectoryScope, DirectoryInventoryConnection? Connection = null,
    string? Search = null, int Page = 1, int PageSize = 50, bool Refresh = false);
