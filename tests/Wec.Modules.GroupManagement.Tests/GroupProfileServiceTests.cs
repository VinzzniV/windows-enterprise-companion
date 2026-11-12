using NSubstitute;
using Wec.Core.Contracts;
using Wec.Core.Microsoft365;
using Wec.Core.Objects;
using Wec.Core.Results;

namespace Wec.Modules.GroupManagement.Tests;

public sealed class GroupProfileServiceTests
{
    private const string Tenant = "11111111-1111-1111-1111-111111111111";
    private const string Id = "22222222-2222-2222-2222-222222222222";
    private const string MemberId = "33333333-3333-3333-3333-333333333333";
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private readonly IDirectoryGroupReadProvider _directory = Substitute.For<IDirectoryGroupReadProvider>();
    private readonly IMicrosoft365GroupContextProvider _cloud = Substitute.For<IMicrosoft365GroupContextProvider>();
    private static ObjectReference AdReference => new(ObjectKind.Group, ObjectSource.ActiveDirectory, "example.test", Id);
    private static ObjectReference CloudReference => new(ObjectKind.Group, ObjectSource.Entra, Tenant, Id);
    private GroupProfileService Create() => new(_directory, _cloud);
    private static DirectoryGroupReadState DirectoryState => new(Now, Now, null, 1, 1, Now.AddHours(1), Now.AddMinutes(10), false);
    private static Microsoft365ReadState State(Microsoft365Resource resource) => new(new(resource, Id), Tenant, 1, 1,
        Microsoft365Availability.Available, false, Now, Now, null, Now.AddHours(1), EvidenceFreshness.Fresh, EvidenceCoverage.Partial, 3, null);
    private static DirectoryGroupRecord Group => new(Guid.Parse(Id), null, "example.test", "Operations", "Ops", "CN=Operations,DC=example,DC=test", null, null, null);

    public GroupProfileServiceTests()
    {
        _directory.ReadCachedIdentityAsync(Arg.Any<DirectoryGroupIdentityQuery>(), Arg.Any<CancellationToken>())
            .Returns(new CachedDirectoryGroupIdentity(DirectoryState, new("example.test", Now, [Group], false)));
        _directory.ReadCachedMembersAsync(Arg.Any<DirectoryGroupMemberQuery>(), Arg.Any<CancellationToken>())
            .Returns(new CachedDirectoryGroupMembers(DirectoryState, new("example.test", Guid.Parse(Id), Now, 1, 50, 2,
                [new(Guid.Parse(MemberId), null, ObjectKind.User, "user", "Member", null, null, "CN=Member,DC=example,DC=test"),
                    new(null, null, null, "foreignSecurityPrincipal", "Limited object", null, null, "CN=Unknown,DC=example,DC=test")], "Direct only")));
        _cloud.ReadGroupCachedAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Result.Success(new Microsoft365GroupContext(Tenant, 1, 1,
            [new(State(Microsoft365Resource.Group), [new(Id, "Operations", null, null, null, null, null, null)])],
            new(State(Microsoft365Resource.GroupMembers), [new(MemberId, null, "user", null), new(null, "Limited", null, null), new(Id, "Nested group", "group", null)]))));
    }

    [Fact]
    public async Task CloudGroupRemainsIndependentOfSameNamedDirectoryGroupAndLimitedMembersRemainVisible()
    {
        var profile = (await Create().GetAsync(new(CloudReference), CancellationToken.None)).Value;
        Assert.Equal("Operations", profile.Title);
        Assert.Equal(IdentityEvidence.ScopedId, profile.Identity);
        Assert.Null(profile.Directory);
        Assert.Equal(3, profile.Cloud!.DirectMembers!.Members.Count);
        Assert.Equal(2, profile.Relationships.Count);
        Assert.Contains(profile.Relationships, link => link.Target.Kind == ObjectKind.User && link.Label == MemberId);
        Assert.Contains(profile.Relationships, link => link.Target.Kind == ObjectKind.Group && link.Target.Id == Id);
        Assert.Empty(_directory.ReceivedCalls());
    }

    [Fact]
    public async Task DirectoryProfileUsesCachedDataAndNativeMemberGuidWithoutCloudOrRemoteReads()
    {
        var profile = (await Create().GetAsync(new(AdReference), CancellationToken.None)).Value;
        var link = Assert.Single(profile.Relationships);
        Assert.Equal(new ObjectReference(ObjectKind.User, ObjectSource.ActiveDirectory, "example.test", MemberId), link.Target);
        Assert.Equal(2, profile.DirectoryMembers!.Data!.Members.Count);
        Assert.Empty(_cloud.ReceivedCalls());
        await _directory.DidNotReceiveWithAnyArgs().ReadIdentityAsync(default!, default);
        await _directory.DidNotReceiveWithAnyArgs().ReadMembersAsync(default!, default);
    }

    [Fact]
    public async Task ExplicitMemberReadUsesRequestedPageAndNeverExpandsNestedGroups()
    {
        _directory.ReadMembersAsync(Arg.Any<DirectoryGroupMemberQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new DirectoryGroupMemberPage("example.test", Guid.Parse(Id), Now, 2, 25, 0, [], "Direct only")));
        Assert.True((await Create().GetAsync(new(AdReference, Read: GroupProfileRead.DirectoryMembers, MemberPage: 2, MemberPageSize: 25), CancellationToken.None)).IsSuccess);
        await _directory.Received(1).ReadMembersAsync(Arg.Is<DirectoryGroupMemberQuery>(query => query.GroupObjectId == Guid.Parse(Id) && query.Page == 2 && query.PageSize == 25), CancellationToken.None);
        await _directory.DidNotReceiveWithAnyArgs().ReadIdentityAsync(default!, default);
    }

    [Fact]
    public async Task DuplicateGroupIdentityDoesNotAttachAutomaticMemberLinks()
    {
        _directory.ReadCachedIdentityAsync(Arg.Any<DirectoryGroupIdentityQuery>(), Arg.Any<CancellationToken>())
            .Returns(new CachedDirectoryGroupIdentity(DirectoryState, new("example.test", Now, [Group, Group], false)));
        var profile = (await Create().GetAsync(new(AdReference), CancellationToken.None)).Value;
        Assert.Equal(IdentityEvidence.Ambiguous, profile.Identity);
        Assert.Empty(profile.Relationships);
        Assert.Equal(2, profile.Directory!.Data!.Groups.Count);
    }

    [Fact]
    public async Task MixedDirectorySessionCannotProduceAProfile()
    {
        _directory.ReadCachedMembersAsync(Arg.Any<DirectoryGroupMemberQuery>(), Arg.Any<CancellationToken>())
            .Returns(new CachedDirectoryGroupMembers(DirectoryState with { SessionRevision = 2 }, null));
        Assert.True((await Create().GetAsync(new(AdReference), CancellationToken.None)).IsFailure);
    }

    [Fact]
    public async Task ExactDnResolutionSelectsOnlyTheReturnedScopedGuid()
    {
        _directory.ReadIdentityAsync(Arg.Any<DirectoryGroupIdentityQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new DirectoryGroupIdentityResult("example.test", Now, [Group], false)));
        var result = await Create().ResolveAsync(new("example.test", DistinguishedName: Group.DistinguishedName), CancellationToken.None);
        Assert.Equal(AdReference, result.Value);
        await _directory.Received(1).ReadIdentityAsync(Arg.Is<DirectoryGroupIdentityQuery>(query => query.DistinguishedName == Group.DistinguishedName && query.ObjectId == null), CancellationToken.None);
    }

    [Fact]
    public async Task AmbiguousDnResolutionCannotSelectFirstGroup()
    {
        _directory.ReadIdentityAsync(Arg.Any<DirectoryGroupIdentityQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new DirectoryGroupIdentityResult("example.test", Now, [Group, Group], false)));
        Assert.True((await Create().ResolveAsync(new("example.test", DistinguishedName: Group.DistinguishedName), CancellationToken.None)).IsFailure);
    }

    [Fact]
    public async Task InvalidNativeReferenceAndCrossSourceReadStopBeforeProviderAccess()
    {
        Assert.True((await Create().GetAsync(new(CloudReference with { Scope = "unknown" }), CancellationToken.None)).IsFailure);
        Assert.True((await Create().GetAsync(new(CloudReference, Read: GroupProfileRead.DirectoryMembers), CancellationToken.None)).IsFailure);
        Assert.Empty(_cloud.ReceivedCalls());
        Assert.Empty(_directory.ReceivedCalls());
    }
}
