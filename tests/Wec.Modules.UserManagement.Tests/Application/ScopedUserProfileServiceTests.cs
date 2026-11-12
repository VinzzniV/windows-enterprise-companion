using Microsoft.Extensions.Options;
using NSubstitute;
using Wec.Core.Contracts;
using Wec.Core.Microsoft365;
using Wec.Core.Objects;
using Wec.Core.Results;
using Wec.Modules.UserManagement.Application;

namespace Wec.Modules.UserManagement.Tests.Application;

public sealed class ScopedUserProfileServiceTests
{
    private const string Tenant = "11111111-1111-1111-1111-111111111111";
    private const string CloudId = "22222222-2222-2222-2222-222222222222";
    private const string OtherId = "33333333-3333-3333-3333-333333333333";
    private const string Sid = "S-1-5-21-1-2-3-1001";
    private static readonly Guid AdId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private readonly IDirectoryUserSnapshotProvider _directory = Substitute.For<IDirectoryUserSnapshotProvider>();
    private readonly IMicrosoft365UserContextProvider _cloud = Substitute.For<IMicrosoft365UserContextProvider>();
    private readonly IUserDeviceRelationshipProvider _devices = Substitute.For<IUserDeviceRelationshipProvider>();
    private Microsoft365UserContext _context = Context();
    private static ObjectReference AdReference => new(ObjectKind.User, ObjectSource.ActiveDirectory, "example.test", AdId.ToString("D"));
    private static ObjectReference CloudReference => new(ObjectKind.User, ObjectSource.Entra, Tenant, CloudId);

    public ScopedUserProfileServiceTests()
    {
        _cloud.ReadCachedAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Result.Success(_context));
        _devices.GetForDirectorySidAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new UserDeviceRelationshipSnapshot(new(0, 0, 0, 0, 0), []));
    }

    private ScopedUserProfileService Create() => new(_directory, _cloud, new UserManagementService(
        Substitute.For<IDirectoryUserReadProvider>(), _devices, Substitute.For<IInstalledSoftwareInventoryProvider>(),
        Substitute.For<IDeviceHealthSnapshotProvider>(), Substitute.For<ISecurityReportDataProvider>(),
        Substitute.For<IStoredNessusComputerInventoryProvider>(), Options.Create(new UserManagementOptions())), new("workspace", "LOCAL"));

    private static Microsoft365User User(string? sid = Sid, string id = CloudId, string upn = "user@example.test") =>
        new(id, "Cloud name", upn, null, null, "Guest", null, null, null, null, sid, "never-assume-guid-anchor", null);
    private static Microsoft365ReadState State(Microsoft365Resource resource, EvidenceCoverage coverage = EvidenceCoverage.ReturnedSet,
        string? sid = null, string? id = null) => new(new(resource, id, sid), Tenant, 1, 1, Microsoft365Availability.Available,
            false, Now, Now, null, Now.AddHours(1), EvidenceFreshness.Fresh, coverage, 1, null);
    private static Microsoft365UserContext Context(params CachedEntraUsers[] reads) => new(Tenant, 1, 1, reads,
        new(State(Microsoft365Resource.Licenses), []), null, null, null, [], null, null);
    private static DirectoryUserRecord AdUser() => new(AdId, Sid, "AD name", "user", "user@example.test", null, null,
        "AD department", null, null, "CN=user,DC=example,DC=test", "DC=example,DC=test", null,
        null, null, null, null, null, null, [], new(DirectoryUserAccessCoverage.Unavailable, "Not evaluated", []), "example.test");
    private void CacheAd() => _directory.ReadCachedAsync(Arg.Any<DirectoryUserLookup>(), Arg.Any<CancellationToken>())
        .Returns(new CachedDirectoryUser(new("example.test", Now, AdUser()), Now, null, 1, 1, Now.AddHours(1), false, Now.AddMinutes(10)));

    [Fact]
    public async Task EntraOnlyGuestProfileDoesNotQueryDirectoryOrInventWindowsEvidence()
    {
        _context = Context(new CachedEntraUsers(State(Microsoft365Resource.User, id: CloudId), [User(sid: null)]));
        var result = (await Create().GetAsync(new(CloudReference), CancellationToken.None)).Value;
        Assert.Equal("Cloud name", result.Title);
        Assert.Equal(IdentityEvidence.ScopedId, result.Identity);
        Assert.Equal("Guest", result.EntraUser!.UserType);
        Assert.Null(result.AdProfile);
        Assert.Null(result.EntraUser.AccountEnabled);
        Assert.Empty(_directory.ReceivedCalls());
        Assert.Empty(_devices.ReceivedCalls());
    }

    [Fact]
    public async Task ExactSidReadConfirmsHybridAccountDespitePartialInventoryAndPreservesSourceFields()
    {
        CacheAd();
        _context = Context(new CachedEntraUsers(State(Microsoft365Resource.Users, EvidenceCoverage.Partial), [User()]),
            new(State(Microsoft365Resource.UsersBySid, sid: Sid), [User()]));
        var result = (await Create().GetAsync(new(AdReference), CancellationToken.None)).Value;
        Assert.Equal("AD name", result.Title);
        Assert.Equal("AD department", result.AdProfile!.Identity.Department);
        Assert.Equal("Cloud name", result.EntraUser!.DisplayName);
        Assert.Contains(result.Relationships, link => link.Target == CloudReference && link.Evidence == IdentityEvidence.ScopedId);
        await _directory.DidNotReceive().ReadIdentityAsync(Arg.Any<DirectoryUserLookup>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MatchingSidInPartialInventoryRemainsCandidate()
    {
        CacheAd();
        _context = Context(new CachedEntraUsers(State(Microsoft365Resource.Users, EvidenceCoverage.Partial), [User()]));
        var result = (await Create().GetAsync(new(AdReference), CancellationToken.None)).Value;
        Assert.Null(result.EntraUser);
        Assert.Null(result.Cloud!.UserLicenses);
        Assert.Equal(IdentityEvidence.Ambiguous, Assert.Single(result.Candidates).Evidence);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DuplicateSidNeverSelectsAnAccount(bool duplicateSameId)
    {
        CacheAd();
        _context = Context(new CachedEntraUsers(State(Microsoft365Resource.Users), [User(), User(id: duplicateSameId ? CloudId : OtherId)]));
        var result = (await Create().GetAsync(new(AdReference), CancellationToken.None)).Value;
        Assert.Null(result.EntraUser);
        Assert.Empty(result.Relationships);
        Assert.All(result.Candidates, link => Assert.Equal(IdentityEvidence.Ambiguous, link.Evidence));
    }

    [Fact]
    public async Task UpnWithDifferentSidIsConflictAndUnverifiedImmutableIdCannotOverrideIt()
    {
        CacheAd();
        _context = Context(new CachedEntraUsers(State(Microsoft365Resource.Users), [User("S-1-5-21-1-2-3-1002")]));
        var result = (await Create().GetAsync(new(AdReference), CancellationToken.None)).Value;
        Assert.Null(result.EntraUser);
        Assert.Equal(IdentityEvidence.Conflict, Assert.Single(result.Candidates).Evidence);
    }

    [Fact]
    public async Task SidAndUpnRenameStillConfirmSameAccount()
    {
        CacheAd();
        _context = Context(new CachedEntraUsers(State(Microsoft365Resource.Users), [User(upn: "renamed@example.test")]));
        var result = (await Create().GetAsync(new(AdReference), CancellationToken.None)).Value;
        Assert.Equal("renamed@example.test", result.EntraUser!.UserPrincipalName);
        Assert.Equal("user@example.test", result.AdProfile!.Identity.UserPrincipalName);
    }

    [Fact]
    public async Task InverseResolutionRequiresExplicitDirectoryScopeAndKeepsUniqueScopedGuid()
    {
        CacheAd();
        _context = Context(new CachedEntraUsers(State(Microsoft365Resource.Users), [User()]));
        var first = (await Create().GetAsync(new(CloudReference), CancellationToken.None)).Value;
        Assert.Null(first.AdProfile);
        Assert.Empty(_directory.ReceivedCalls());
        _directory.ReadIdentityAsync(Arg.Any<DirectoryUserLookup>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new DirectoryUserIdentityResult("example.test", Now, AdUser())));
        var result = (await Create().GetAsync(new(CloudReference, LoadDirectoryIdentity: true, DirectoryScope: "example.test"), CancellationToken.None)).Value;
        Assert.NotNull(result.AdProfile);
        Assert.Contains(result.Relationships, link => link.Target == AdReference);
        await _directory.Received(1).ReadIdentityAsync(Arg.Is<DirectoryUserLookup>(query => query.DirectoryScope == "example.test"
            && query.SecurityIdentifier == Sid && query.ObjectId == null), CancellationToken.None);
    }

    [Fact]
    public async Task CloudRelationshipsKeepDirectGroupsRegisteredDevicesAndAssociatedIntuneSeparate()
    {
        _context = Context(new CachedEntraUsers(State(Microsoft365Resource.User, id: CloudId), [User(null)])) with
        {
            DirectGroups = new(State(Microsoft365Resource.UserGroups), [new(OtherId, "Group", null, null, null, null, null, null)]),
            RegisteredDevices = new(State(Microsoft365Resource.UserDevices), [new(OtherId, null, "Device", null, null, null, null, null)]),
            AssociatedIntune = [new(State(Microsoft365Resource.ManagedDevices), [new(OtherId, "Device", CloudId, null, null, null, null, null, null, null, null, null, null, null)])],
        };
        var result = (await Create().GetAsync(new(CloudReference), CancellationToken.None)).Value;
        Assert.Equal(3, result.Relationships.Count);
        Assert.Contains(result.Relationships, link => link.Target.Kind == ObjectKind.Group);
        Assert.Contains(result.Relationships, link => link.Relation == "Associated user (Intune)" && link.Target.Source == ObjectSource.Intune);
    }

    [Fact]
    public async Task ConflictingIntuneUserObservationsBecomeCandidatesWithQueryProvenance()
    {
        var old = new Microsoft365ManagedDevice(OtherId, "Device", CloudId, null, null, null, null, null, null, null, null, null, null, null);
        _context = Context(new CachedEntraUsers(State(Microsoft365Resource.User, id: CloudId), [User(null)])) with
        {
            AssociatedIntune = [new(State(Microsoft365Resource.ManagedDevices), [old]),
                new(State(Microsoft365Resource.ManagedDevice, id: OtherId), [old with { UserId = OtherId }])],
        };
        var result = (await Create().GetAsync(new(CloudReference), CancellationToken.None)).Value;
        Assert.Empty(result.Relationships);
        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(IdentityEvidence.Conflict, candidate.Evidence);
        Assert.Contains("ManagedDevices", candidate.Explanation, StringComparison.Ordinal);
        Assert.Contains(OtherId, candidate.Explanation, StringComparison.Ordinal);
        Assert.Equal(2, result.Cloud!.AssociatedIntune.Count);
    }

    [Fact]
    public async Task SessionChangeDuringStoredDeviceCompositionDiscardsMixedResult()
    {
        CacheAd();
        _context = Context(new CachedEntraUsers(State(Microsoft365Resource.Users), [User()]));
        _devices.GetForDirectorySidAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _context = _context with { SessionRevision = 2 };
            return new UserDeviceRelationshipSnapshot(new(0, 0, 0, 0, 0), []);
        });
        var result = await Create().GetAsync(new(AdReference), CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Microsoft365NotConnected, result.Error!.Code);
    }

    [Fact]
    public async Task InvalidReferenceCannotReachEitherSource()
    {
        var result = await Create().GetAsync(new(CloudReference with { Scope = "another tenant" }), CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Empty(_cloud.ReceivedCalls());
        Assert.Empty(_directory.ReceivedCalls());
    }
}
