using Wec.Core.Contracts;
using Wec.Modules.Inventory.Application;
using Wec.Modules.Inventory.Domain;
using Wec.Modules.Inventory.Persistence;

namespace Wec.Modules.Inventory.Tests.Application;

public sealed class DeviceCleanupInventoryEvidenceProviderTests
{
    private static readonly DateTimeOffset CapturedAt =
        new(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Project_ExposesApprovedObservationsWithoutOwnershipClaims()
    {
        CachedHardwareSnapshot cached = Cached(new DeviceUserEvidence(
            UserEvidenceSourceState.Available,
            new InteractiveDomainUserEvidence("S-1-5-21-1-2-3-1104", "CORP", "alex"),
            null,
            UserEvidenceSourceState.Available,
            [new LocalUserProfileEvidence("S-1-5-21-1-2-3-1104", CapturedAt.AddDays(-5))],
            null,
            LocalProfilesTruncated: false));

        DeviceCleanupInventoryEvidence projected =
            DeviceCleanupInventoryEvidenceProvider.Project("PC-42", cached);

        Assert.Equal(CapturedAt, projected.CapturedAtUtc);
        Assert.Equal(DeviceCleanupUserEvidenceAvailability.Available, projected.UserEvidenceAvailability);
        Assert.Equal(2, projected.UserObservations.Count);
        Assert.Equal("CORP\\alex", projected.UserObservations[0].AccountDisplay);
        Assert.Equal("High", projected.UserObservations[0].Confidence);
        Assert.Contains("not an ownership claim", projected.UserObservations[0].Explanation);
        Assert.Null(projected.UserObservations[1].AccountDisplay);
        Assert.Equal(CapturedAt.AddDays(-5), projected.UserObservations[1].ProfileLastUseAtUtc);
    }

    [Fact]
    public void Project_ReportsLegacyAndPartialCoverageExplicitly()
    {
        DeviceCleanupInventoryEvidence legacy =
            DeviceCleanupInventoryEvidenceProvider.Project("PC-OLD", Cached(null));
        Assert.Equal(DeviceCleanupUserEvidenceAvailability.NotCaptured, legacy.UserEvidenceAvailability);

        DeviceCleanupInventoryEvidence partial = DeviceCleanupInventoryEvidenceProvider.Project(
            "PC-PARTIAL",
            Cached(new DeviceUserEvidence(
                UserEvidenceSourceState.Unavailable,
                null,
                new UserEvidenceCaptureError("WMI_UNAVAILABLE", "Unavailable."),
                UserEvidenceSourceState.Available,
                [],
                null,
                LocalProfilesTruncated: false)));
        Assert.Equal(DeviceCleanupUserEvidenceAvailability.Partial, partial.UserEvidenceAvailability);
        Assert.Contains("one evidence source", partial.UserEvidenceExplanation);
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
