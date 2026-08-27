using NSubstitute;
using Wec.Modules.Inventory.Application;
using Wec.Modules.Inventory.Domain;
using Wec.Modules.Inventory.Persistence;

namespace Wec.Modules.Inventory.Tests.Application;

public sealed class InstalledSoftwareInventoryProviderTests
{
    private static readonly DateTimeOffset CapturedAt = new(2026, 8, 26, 10, 0, 0, TimeSpan.Zero);
    private readonly IHardwareSnapshotRepository _repository = Substitute.For<IHardwareSnapshotRepository>();

    [Fact]
    public async Task LatestStoredSoftware_MapsForOneRemoteHost()
    {
        _repository.GetLatestAsync(
                Wec.Core.Targets.ScanTarget.Remote("PC-42").CacheKey,
                Arg.Any<CancellationToken>())
            .Returns(new CachedHardwareSnapshot(
                Snapshot(
                    [new InstalledSoftwareEntry("7-Zip", "24.09", "Igor Pavlov")],
                    error: null),
                CapturedAt));
        var provider = new InstalledSoftwareInventoryProvider(_repository);

        Wec.Core.Contracts.InstalledSoftwareSnapshotData? result = await provider.GetLatestAsync(
            "PC-42",
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.IsComplete);
        Assert.Equal("7-Zip", Assert.Single(result.Software).Name);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public async Task FailedSoftwareCapture_RemainsDistinctFromAnEmptyInventory()
    {
        _repository.GetLatestAsync(
                Wec.Core.Targets.ScanTarget.Local.CacheKey,
                Arg.Any<CancellationToken>())
            .Returns(new CachedHardwareSnapshot(
                Snapshot(
                    software: null,
                    new SoftwareCaptureError("ACCESS_DENIED", "Registry access denied.")),
                CapturedAt));
        var provider = new InstalledSoftwareInventoryProvider(_repository);

        Wec.Core.Contracts.InstalledSoftwareSnapshotData? result = await provider.GetLatestAsync(
            host: null,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(result.IsComplete);
        Assert.Empty(result.Software);
        Assert.Equal("ACCESS_DENIED", result.ErrorCode);
    }

    private static HardwareSnapshot Snapshot(
        IReadOnlyList<InstalledSoftwareEntry>? software,
        SoftwareCaptureError? error) => new(
        new CpuInfo("CPU", 4, 8, 3000),
        [new MemoryBank("Memory", "M-1", 8_589_934_592, 3200)],
        [new DiskDrive("Disk", 256_000_000_000, "NVMe", "Fixed")],
        new OperatingSystemInfo("Windows 11", "10.0", "26100", "64-bit"),
        InstalledSoftware: software,
        InstalledSoftwareError: error);
}
