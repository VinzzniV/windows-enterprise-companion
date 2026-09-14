using Microsoft.Extensions.Options;
using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Microsoft365;
using Wec.Core.Objects;
using Wec.Core.Results;
using Wec.Modules.Clients.Application;

namespace Wec.Modules.Clients.Tests.Application;

public sealed class DeviceProfileServiceTests
{
    private const string TenantId = "11111111-1111-1111-1111-111111111111";
    private const string ObjectId = "22222222-2222-2222-2222-222222222222";
    private const string DeviceId = "33333333-3333-3333-3333-333333333333";
    private const string UserId = "44444444-4444-4444-4444-444444444444";
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private readonly IInventoryClientSnapshotProvider _hosts = Substitute.For<IInventoryClientSnapshotProvider>();
    private readonly ISavedClientTargetProvider _saved = Substitute.For<ISavedClientTargetProvider>();
    private readonly IDirectoryComputerReadProvider _directory = Substitute.For<IDirectoryComputerReadProvider>();
    private readonly IManagementDeviceSnapshotProvider _management = Substitute.For<IManagementDeviceSnapshotProvider>();
    private readonly IMicrosoft365DeviceContextProvider _cloud = Substitute.For<IMicrosoft365DeviceContextProvider>();
    private readonly IInventoryReportDataProvider _inventory = Substitute.For<IInventoryReportDataProvider>();

    public DeviceProfileServiceTests()
    {
        _hosts.ListHostsAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<InventoryClientSnapshotHost>());
        _saved.ListClientsAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<SavedClientTarget>());
        Cloud();
    }

    private DeviceProfileService Create()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        var overview = new ClientOverviewService(_inventory, Substitute.For<IInstalledSoftwareInventoryProvider>(),
            Substitute.For<IDeviceHealthSnapshotProvider>(), Substitute.For<ISecurityReportDataProvider>(),
            Substitute.For<IClientUserRelationshipProvider>(), clock, Options.Create(new ClientOverviewOptions()));
        return new(new("workspace", Environment.MachineName), overview, _hosts, _saved, _directory, _management, _cloud);
    }

    private static ObjectReference Reference(ObjectSource source = ObjectSource.Entra, string id = ObjectId) =>
        new(ObjectKind.Device, source, source == ObjectSource.Wec ? "workspace" : source == ObjectSource.ActiveDirectory ? "example.test" : TenantId, id);

    private static Microsoft365ReadState State(Microsoft365Resource resource, bool partial = false) => new(new(resource), TenantId,
        1, 1, Microsoft365Availability.Available, false, Now, Now, null, Now.AddHours(1), EvidenceFreshness.Fresh,
        partial ? EvidenceCoverage.Partial : EvidenceCoverage.ReturnedSet, 0, null);

    private void Cloud(Microsoft365Device[]? devices = null, Microsoft365ManagedDevice[]? managed = null, bool partial = false)
    {
        var context = new Microsoft365DeviceContext(TenantId, 1, 1,
            [new(State(Microsoft365Resource.Devices, partial), devices ?? [])], new(State(Microsoft365Resource.ManagedDevices), managed ?? []),
            new(State(Microsoft365Resource.DeviceOwners), [new(UserId, "Owner", "user", null)]));
        _cloud.ReadCachedAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>(), Arg.Any<string?>()).Returns(Result.Success(context));
    }

    private static Microsoft365Device Device(string id = ObjectId, string name = "PC") => new(id, DeviceId, name, null, null, null, null, null);
    private static Microsoft365ManagedDevice Managed(string id = ObjectId) => new(id, "PC", UserId, null, null, null, null, null, null,
        null, null, null, null, DeviceId);

    [Fact]
    public async Task EntraOnlyProfileRetainsScopedAnchorAndNeverLoadsLocalOrDirectoryEvidence()
    {
        Cloud([Device()]);
        var result = await Create().GetAsync(new(Reference()), CancellationToken.None);
        Assert.Equal("PC", result.Value.Title);
        Assert.Equal(Reference(), result.Value.Reference);
        Assert.Equal(IdentityEvidence.ScopedId, result.Value.Identity);
        Assert.Null(result.Value.OperationalHost);
        Assert.Null(result.Value.Wec);
        Assert.Null(result.Value.Cloud!.EntraReads[0].Devices[0].AccountEnabled);
        Assert.Contains(result.Value.Relationships, link => link.Target.Id == UserId && link.Relation == "Registered owner (Entra)");
        await _inventory.DidNotReceiveWithAnyArgs().GetLatestAsync(default, default);
        await _directory.DidNotReceiveWithAnyArgs().ReadIdentityAsync(default!, default);
    }

    [Fact]
    public async Task MultipleIntuneEnrollmentsRemainSeparateWithoutSelectingLatest()
    {
        Cloud([Device()], [Managed(), Managed(DeviceId) with { LastSyncAtUtc = Now.AddDays(1) }]);
        var profile = (await Create().GetAsync(new(Reference()), CancellationToken.None)).Value;
        Assert.Equal(2, profile.Cloud!.Intune.Devices.Count);
        Assert.Equal(2, profile.Relationships.Count(link => link.Relation == "Intune enrollment record"));
        Assert.Contains(profile.Relationships, link => link.Relation == "Associated user (Intune)"
            && link.Explanation.Contains("not a primary-user", StringComparison.Ordinal));
        Assert.Null(profile.OperationalHost);
    }

    [Fact]
    public async Task WecNameMatchDoesNotPromoteCloudOrManagementEvidenceToConfirmed()
    {
        Cloud([Device(name: "pc.example.test")], [Managed()]);
        _management.ReadCachedAsync(Arg.Any<DirectoryInventoryConnection?>(), Arg.Any<KasperskyInventoryConnection?>(), Arg.Any<CancellationToken>())
            .Returns(new ManagementDeviceSnapshot(Now, [], [],
                [new("PC", "pc.other.test", null, null, null, null, null, null), new("PC", "pc.other.test", null, null, null, null, null, null)], [], []));
        var profile = (await Create().GetAsync(new(Reference(ObjectSource.Wec, "pc.example.test")), CancellationToken.None)).Value;
        Assert.Empty(profile.Relationships);
        Assert.Equal(2, profile.ManagementCandidates!.Kaspersky.Count);
        Assert.Contains(profile.Candidates, candidate => candidate.Target.Source == ObjectSource.Entra);
        Assert.Equal(IdentityEvidence.AddressCandidate, profile.Identity);
    }

    [Fact]
    public async Task ShortHostCollisionRequiresSelectionAndDoesNotReadLocalSnapshot()
    {
        _hosts.ListHostsAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            new InventoryClientSnapshotHost("PC.alpha.test", Now), new InventoryClientSnapshotHost("PC.beta.test", Now),
        });
        var profile = (await Create().GetAsync(new(Reference(ObjectSource.Wec, "PC")), CancellationToken.None)).Value;
        Assert.Equal(IdentityEvidence.Ambiguous, profile.Identity);
        Assert.Equal(2, profile.Candidates.Count);
        Assert.Null(profile.OperationalHost);
        await _inventory.DidNotReceiveWithAnyArgs().GetLatestAsync(default, default);
    }

    [Fact]
    public async Task ExactStoredHostWinsBeforeAliasesWithoutChangingItsStorageKey()
    {
        _hosts.ListHostsAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            new InventoryClientSnapshotHost("PC", Now), new InventoryClientSnapshotHost("PC.alpha.test", Now),
        });
        var profile = (await Create().GetAsync(new(Reference(ObjectSource.Wec, "pc")), CancellationToken.None)).Value;
        Assert.Equal("PC", profile.OperationalHost);
        await _inventory.Received(1).GetLatestAsync("PC", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IntuneOnlyUserRelationDoesNotDependOnEntraDeviceInventory()
    {
        Cloud(managed: [Managed()]);
        var profile = (await Create().GetAsync(new(Reference(ObjectSource.Intune)), CancellationToken.None)).Value;
        Assert.Equal(IdentityEvidence.ScopedId, profile.Identity);
        Assert.Equal(UserId, Assert.Single(profile.Relationships).Target.Id);
        Assert.Null(profile.Wec);
    }

    [Fact]
    public async Task ExactIntuneDetailProvidesAProfileWithoutCollectionRead()
    {
        var context = new Microsoft365DeviceContext(TenantId, 1, 2, [], new(State(Microsoft365Resource.ManagedDevices), []), null)
        {
            ManagedDetails = [new(State(Microsoft365Resource.ManagedDevice) with { Query = new(Microsoft365Resource.ManagedDevice, ObjectId) }, [Managed()])],
        };
        _cloud.ReadCachedAsync(TenantId, null, Arg.Any<CancellationToken>(), ObjectId).Returns(Result.Success(context));
        var profile = (await Create().GetAsync(new(Reference(ObjectSource.Intune)), CancellationToken.None)).Value;
        Assert.Equal("PC", profile.Title);
        Assert.Equal(IdentityEvidence.ScopedId, profile.Identity);
        Assert.Equal(UserId, Assert.Single(profile.Relationships).Target.Id);
        Assert.Empty(profile.Cloud!.Intune.Devices);
    }

    [Fact]
    public async Task PartialEntraSetDoesNotProveUniqueInverseDeviceRelationship()
    {
        Cloud([Device()], [Managed()], partial: true);
        var profile = (await Create().GetAsync(new(Reference(ObjectSource.Intune)), CancellationToken.None)).Value;
        Assert.DoesNotContain(profile.Relationships, link => link.Target.Kind == ObjectKind.Device);
        Assert.Contains(profile.Candidates, link => link.Target.Source == ObjectSource.Entra && link.Evidence == IdentityEvidence.Ambiguous);
    }

    [Fact]
    public async Task ConflictingDeviceIdsDisableIntuneConfirmation()
    {
        Cloud([Device(), Device(UserId)], [Managed()]);
        var profile = (await Create().GetAsync(new(Reference()), CancellationToken.None)).Value;
        Assert.Equal(IdentityEvidence.Conflict, profile.Identity);
        Assert.DoesNotContain(profile.Relationships, link => link.Target.Source == ObjectSource.Intune);
    }

    [Fact]
    public async Task DuplicateAnchorRecordsStayAmbiguous()
    {
        Cloud([Device(), Device()], [Managed()]);
        var profile = (await Create().GetAsync(new(Reference()), CancellationToken.None)).Value;
        Assert.Equal(IdentityEvidence.Ambiguous, profile.Identity);
        Assert.Equal(2, profile.Cloud!.EntraReads[0].Devices.Count);
        Assert.Empty(profile.Relationships);
    }

    [Fact]
    public async Task MissingCloudSourceIsAnUnresolvedProfileNotALocalFallback()
    {
        var profile = (await Create().GetAsync(new(Reference()), CancellationToken.None)).Value;
        Assert.Equal(IdentityEvidence.Unresolved, profile.Identity);
        Assert.Equal(ObjectId, profile.Title);
        Assert.Null(profile.OperationalHost);
        await _inventory.DidNotReceiveWithAnyArgs().GetLatestAsync(default, default);
    }

    [Fact]
    public async Task WrongWorkspaceAndTenantDoNotExposeOtherEvidence()
    {
        Assert.True((await Create().GetAsync(new(Reference(ObjectSource.Wec, "PC") with { Scope = "another" }), CancellationToken.None)).IsFailure);
        _cloud.ReadCachedAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>(), Arg.Any<string?>())
            .Returns(Result.Failure<Microsoft365DeviceContext>(new(ErrorCode.Microsoft365NotConnected, "Wrong context")));
        Assert.True((await Create().GetAsync(new(Reference()), CancellationToken.None)).IsFailure);
    }

    [Fact]
    public async Task AdSourceLoadRequiresExplicitRequestAndPreservesFailureMetadata()
    {
        var reference = Reference(ObjectSource.ActiveDirectory);
        _directory.ReadIdentityAsync(Arg.Any<DirectoryComputerIdentityQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<DirectoryComputerIdentityResult>(new(ErrorCode.AccessDenied, "Denied")));
        await Create().GetAsync(new(reference), CancellationToken.None);
        await _directory.DidNotReceiveWithAnyArgs().ReadIdentityAsync(default!, default);
        var loaded = await Create().GetAsync(new(reference, LoadDirectoryIdentity: true), CancellationToken.None);
        Assert.Equal(ErrorCode.AccessDenied, Assert.Single(loaded.Value.SourceErrors).Code);
        await _directory.Received(1).ReadIdentityAsync(Arg.Is<DirectoryComputerIdentityQuery>(query =>
            query.DirectoryScope == "example.test" && query.ObjectId == Guid.Parse(ObjectId)), Arg.Any<CancellationToken>());
    }
}
