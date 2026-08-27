using Wec.Core.Contracts;
using NSubstitute;
using Wec.Modules.Inventory.Application;
using Wec.Modules.Inventory.Domain;
using Wec.Modules.Inventory.Persistence;

namespace Wec.Modules.Inventory.Tests.Application;

public sealed class InventoryClientUserRelationshipProviderTests
{
    private static readonly DateTimeOffset CapturedAt =
        new(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetLatest_ReadsOnlyTheNormalizedStoredHost()
    {
        IHardwareSnapshotRepository repository = Substitute.For<IHardwareSnapshotRepository>();
        repository.GetLatestAsync("PC-42", Arg.Any<CancellationToken>())
            .Returns(Cached(null));
        var provider = new InventoryClientUserRelationshipProvider(repository);

        ClientUserRelationshipSnapshot? result = await provider.GetLatestAsync(
            " pc-42 ",
            CancellationToken.None);

        Assert.NotNull(result);
        await repository.Received(1).GetLatestAsync("PC-42", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Project_ExposesNamedInteractiveEvidenceAndAggregatesUnresolvedProfiles()
    {
        CachedHardwareSnapshot cached = Cached(new DeviceUserEvidence(
            UserEvidenceSourceState.Available,
            new InteractiveDomainUserEvidence("S-1-5-21-1-2-3-1104", "CORP", "alex"),
            null,
            UserEvidenceSourceState.Available,
            [
                new LocalUserProfileEvidence("S-1-5-21-1-2-3-1104", CapturedAt.AddDays(-1)),
                new LocalUserProfileEvidence("S-1-5-21-1-2-3-2201", CapturedAt.AddDays(-5)),
            ],
            null,
            LocalProfilesTruncated: false));

        ClientUserRelationshipSnapshot projected =
            InventoryClientUserRelationshipProvider.Project(cached);

        ClientObservedUserEvidence observation = Assert.Single(projected.Observations);
        Assert.Equal("CORP\\alex", observation.AccountDisplay);
        Assert.Equal(UserDeviceRelationshipType.LastInteractiveUser, observation.RelationshipType);
        Assert.Equal(UserDeviceRelationshipConfidence.High, observation.Confidence);
        Assert.Contains("not an ownership claim", observation.Explanation, StringComparison.Ordinal);
        Assert.Equal(1, projected.UnresolvedProfileCount);
        Assert.Equal(ClientUserEvidenceAvailability.Available, projected.Availability);
    }

    [Fact]
    public void Project_ReportsLegacyAndUnavailableEvidenceWithoutInventingAUser()
    {
        ClientUserRelationshipSnapshot legacy = InventoryClientUserRelationshipProvider.Project(Cached(null));
        Assert.Equal(ClientUserEvidenceAvailability.NotCaptured, legacy.Availability);
        Assert.Empty(legacy.Observations);

        ClientUserRelationshipSnapshot unavailable = InventoryClientUserRelationshipProvider.Project(Cached(
            new DeviceUserEvidence(
                UserEvidenceSourceState.Unavailable,
                null,
                new UserEvidenceCaptureError("WMI_UNAVAILABLE", "Unavailable."),
                UserEvidenceSourceState.Unavailable,
                null,
                new UserEvidenceCaptureError("WMI_UNAVAILABLE", "Unavailable."),
                LocalProfilesTruncated: false)));
        Assert.Equal(ClientUserEvidenceAvailability.Unavailable, unavailable.Availability);
        Assert.Empty(unavailable.Observations);
    }

    private static CachedHardwareSnapshot Cached(DeviceUserEvidence? evidence) => new(
        new HardwareSnapshot(
            new CpuInfo("CPU", 4, 8, 3000),
            [],
            [],
            new OperatingSystemInfo("Windows 11", "10.0", "26100", "64-bit"),
            UserEvidence: evidence),
        CapturedAt);
}
