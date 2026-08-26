using NSubstitute;
using Wec.Core.Contracts;
using Wec.Modules.Inventory.Application;
using Wec.Modules.Inventory.Domain;
using Wec.Modules.Inventory.Persistence;

namespace Wec.Modules.Inventory.Tests.Application;

public sealed class InventoryUserDeviceRelationshipProviderTests
{
    private static readonly DateTimeOffset CapturedAt = new(2026, 8, 27, 8, 0, 0, TimeSpan.Zero);
    private const string RequestedSid = "S-1-5-21-1-2-3-1104";
    private readonly IHardwareSnapshotRepository _repository = Substitute.For<IHardwareSnapshotRepository>();

    [Fact]
    public async Task ExactSidMatchMergesInteractiveAndProfileEvidencePerDevice()
    {
        _repository.ListHostsAsync(Arg.Any<CancellationToken>()).Returns(
            [new StoredInventoryHost("PC-42", CapturedAt), new StoredInventoryHost("PC-99", CapturedAt)]);
        _repository.GetLatestAsync("PC-42", Arg.Any<CancellationToken>()).Returns(
            Cached(Evidence(
                new InteractiveDomainUserEvidence(RequestedSid, "CORP", "alex"),
                [new LocalUserProfileEvidence(RequestedSid, CapturedAt.AddDays(-1))])));
        _repository.GetLatestAsync("PC-99", Arg.Any<CancellationToken>()).Returns(
            Cached(Evidence(
                new InteractiveDomainUserEvidence("S-1-5-21-1-2-3-2200", "CORP", "alex"),
                [new LocalUserProfileEvidence("S-1-5-21-1-2-3-2200", null)])));
        var provider = new InventoryUserDeviceRelationshipProvider(_repository);

        UserDeviceRelationshipSnapshot result = await provider.GetForDirectorySidAsync(
            RequestedSid,
            CancellationToken.None);

        UserLinkedDeviceEvidence device = Assert.Single(result.Devices);
        Assert.Equal("PC-42", device.Host);
        Assert.Equal(
            [UserDeviceRelationshipType.LastInteractiveUser, UserDeviceRelationshipType.ProfilePresent],
            device.Observations.Select(observation => observation.RelationshipType));
        Assert.Equal(UserDeviceRelationshipConfidence.High, device.Observations[0].Confidence);
        Assert.Equal(UserDeviceRelationshipConfidence.Medium, device.Observations[1].Confidence);
        Assert.All(device.Observations, observation => Assert.Equal("WEC Inventory", observation.Source));
        Assert.Equal(2, result.Coverage.EvidenceCapturedDeviceCount);
    }

    [Fact]
    public async Task CoverageKeepsLegacyUnavailableAndTruncatedSourcesVisible()
    {
        _repository.ListHostsAsync(Arg.Any<CancellationToken>()).Returns(
            [new StoredInventoryHost("LEGACY", CapturedAt), new StoredInventoryHost("PARTIAL", CapturedAt)]);
        _repository.GetLatestAsync("LEGACY", Arg.Any<CancellationToken>()).Returns(Cached(userEvidence: null));
        _repository.GetLatestAsync("PARTIAL", Arg.Any<CancellationToken>()).Returns(Cached(
            new DeviceUserEvidence(
                UserEvidenceSourceState.Unavailable,
                null,
                new UserEvidenceCaptureError("WMI_UNAVAILABLE", "Unavailable."),
                UserEvidenceSourceState.Available,
                [],
                null,
                LocalProfilesTruncated: true)));
        var provider = new InventoryUserDeviceRelationshipProvider(_repository);

        UserDeviceRelationshipSnapshot result = await provider.GetForDirectorySidAsync(
            RequestedSid,
            CancellationToken.None);

        Assert.Empty(result.Devices);
        Assert.Equal(2, result.Coverage.StoredDeviceCount);
        Assert.Equal(1, result.Coverage.EvidenceCapturedDeviceCount);
        Assert.Equal(1, result.Coverage.NotCapturedDeviceCount);
        Assert.Equal(1, result.Coverage.UnavailableDeviceCount);
        Assert.Equal(1, result.Coverage.TruncatedDeviceCount);
    }

    private static CachedHardwareSnapshot Cached(DeviceUserEvidence? userEvidence) =>
        new(Snapshot(userEvidence), CapturedAt);

    private static DeviceUserEvidence Evidence(
        InteractiveDomainUserEvidence? interactive,
        IReadOnlyList<LocalUserProfileEvidence> profiles) => new(
        UserEvidenceSourceState.Available,
        interactive,
        null,
        UserEvidenceSourceState.Available,
        profiles,
        null,
        LocalProfilesTruncated: false);

    private static HardwareSnapshot Snapshot(DeviceUserEvidence? userEvidence) => new(
        new CpuInfo("CPU", 4, 8, 3000),
        [],
        [],
        new OperatingSystemInfo("Windows 11", "10.0", "26100", "64-bit"),
        UserEvidence: userEvidence);
}
