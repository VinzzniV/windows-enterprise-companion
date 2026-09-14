using Microsoft.Extensions.Options;
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
    public async Task ExactSidMatchMergesInteractiveAndProfileEvidencePerDeviceWithOneBoundedRead()
    {
        _repository.GetLatestUserEvidenceBatchAsync(25, Arg.Any<CancellationToken>()).Returns(new StoredInventoryUserEvidenceBatch(2, 2,
            [Record(1, "PC-42", Evidence(new(RequestedSid, "CORP", "alex"), [new(RequestedSid, CapturedAt.AddDays(-1))])),
             Record(2, "PC-99", Evidence(new("S-1-5-21-1-2-3-2200", "CORP", "alex"), [new("S-1-5-21-1-2-3-2200", null)]))]));
        UserDeviceRelationshipSnapshot result = await Provider().GetForDirectorySidAsync(RequestedSid, CancellationToken.None);
        UserLinkedDeviceEvidence device = Assert.Single(result.Devices);
        Assert.Equal("PC-42", device.Host);
        Assert.Equal([UserDeviceRelationshipType.LastInteractiveUser, UserDeviceRelationshipType.ProfilePresent],
            device.Observations.Select(observation => observation.RelationshipType));
        Assert.Equal(UserDeviceRelationshipConfidence.High, device.Observations[0].Confidence);
        Assert.Equal(UserDeviceRelationshipConfidence.Medium, device.Observations[1].Confidence);
        Assert.All(device.Observations, observation => Assert.Equal("WEC Inventory", observation.Source));
        Assert.Equal(2, result.Coverage.EvidenceCapturedDeviceCount);
        Assert.Equal(2, result.Coverage.EvaluatedDeviceCount);
        Assert.False(result.Coverage.WorkingSetTruncated);
        await _repository.Received(1).GetLatestUserEvidenceBatchAsync(25, Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().ListHostsAsync(Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().GetLatestAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CoverageKeepsLegacyUnavailableTruncatedAndUnreadableSourcesVisible()
    {
        _repository.GetLatestUserEvidenceBatchAsync(25, Arg.Any<CancellationToken>()).Returns(new StoredInventoryUserEvidenceBatch(30, 30,
            [Record(1, "LEGACY", null), Record(2, "PARTIAL", new(UserEvidenceSourceState.Unavailable, null,
                new("WMI_UNAVAILABLE", "Unavailable."), UserEvidenceSourceState.Available, [], null, true)),
             new(3, "BAD", CapturedAt, false, null)]));
        UserDeviceRelationshipSnapshot result = await Provider().GetForDirectorySidAsync(RequestedSid, CancellationToken.None);
        Assert.Empty(result.Devices);
        Assert.Equal(30, result.Coverage.StoredDeviceCount);
        Assert.Equal(3, result.Coverage.EvaluatedDeviceCount);
        Assert.True(result.Coverage.WorkingSetTruncated);
        Assert.Equal(1, result.Coverage.EvidenceCapturedDeviceCount);
        Assert.Equal(1, result.Coverage.NotCapturedDeviceCount);
        Assert.Equal(2, result.Coverage.UnavailableDeviceCount);
        Assert.Equal(1, result.Coverage.TruncatedDeviceCount);
    }

    [Fact]
    public async Task EquallyRecentSnapshotsAndDifferentProfileObservationsRemainVisible()
    {
        _repository.GetLatestUserEvidenceBatchAsync(25, Arg.Any<CancellationToken>()).Returns(new StoredInventoryUserEvidenceBatch(1, 2,
            [Record(1, "PC.corp.example", Evidence(null, [new(RequestedSid, null), new(RequestedSid, CapturedAt.AddDays(-1))])),
             Record(2, "PC.corp.example", Evidence(new(RequestedSid, "CORP", "renamed"), []))]));
        UserDeviceRelationshipSnapshot result = await Provider().GetForDirectorySidAsync(RequestedSid, CancellationToken.None);
        Assert.Equal(3, Assert.Single(result.Devices).Observations.Count);
        Assert.Equal(1, result.Coverage.EvaluatedDeviceCount);
        Assert.Equal(1, result.Coverage.EvidenceCapturedDeviceCount);
        Assert.Equal(1, result.Coverage.MultipleLatestSnapshotDeviceCount);
    }

    private InventoryUserDeviceRelationshipProvider Provider() => new(_repository,
        Options.Create(new InventoryOptions { MaxStoredEvidenceRecords = 25 }));
    private static StoredInventoryUserEvidence Record(long id, string host, DeviceUserEvidence? evidence) => new(id, host, CapturedAt, true, evidence);
    private static DeviceUserEvidence Evidence(InteractiveDomainUserEvidence? interactive, IReadOnlyList<LocalUserProfileEvidence> profiles) =>
        new(UserEvidenceSourceState.Available, interactive, null, UserEvidenceSourceState.Available, profiles, null, false);
}
