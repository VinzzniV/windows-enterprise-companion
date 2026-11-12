using Wec.Core.Contracts;
using Wec.Core.Microsoft365;
using Wec.Core.Objects;
using Wec.Core.Results;
using Wec.Modules.UserManagement.Domain;

namespace Wec.Modules.UserManagement.Application;

public sealed record ScopedUserProfileRequest(ObjectReference Reference,
    UserDirectoryConnectionRequest? Connection = null, bool LoadDirectoryIdentity = false,
    string? DirectoryScope = null);

public sealed record ScopedUserProfile(
    ObjectReference Reference, string Title, IdentityEvidence Identity, string Explanation,
    CachedDirectoryUser? Directory, UserProfileResult? AdProfile,
    Microsoft365UserContext? Cloud, Microsoft365User? EntraUser,
    IReadOnlyList<ObjectRelationship> Relationships, IReadOnlyList<ObjectRelationship> Candidates,
    IReadOnlyList<Error> SourceErrors);
