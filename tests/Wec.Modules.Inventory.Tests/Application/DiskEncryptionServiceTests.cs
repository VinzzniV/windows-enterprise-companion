using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.Inventory.Application;
using Wec.Modules.Inventory.Domain;

namespace Wec.Modules.Inventory.Tests.Application;

public class DiskEncryptionServiceTests
{
    private readonly IDiskEncryptionStatusReader _reader = Substitute.For<IDiskEncryptionStatusReader>();

    private DiskEncryptionService CreateService() => new(
        _reader,
        Microsoft.Extensions.Options.Options.Create(new RemoteScanOptions()));

    private void SetUpVolumes(params DiskEncryptionVolume[] volumes) =>
        _reader
            .ReadAsync(
                Arg.Any<ScanTarget>(),
                Arg.Any<ScanCredentials>(),
                Arg.Any<ConnectionOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<DiskEncryptionVolume>>(volumes));

    [Fact]
    public async Task ProviderFailure_IsForwardedWithoutInventingAStatus()
    {
        _reader.ReadAsync(
                Arg.Any<ScanTarget>(),
                Arg.Any<ScanCredentials>(),
                Arg.Any<ConnectionOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<DiskEncryptionVolume>>(
                Error.WmiUnavailable("BitLocker provider unavailable.")));
        DiskEncryptionService service = CreateService();

        Result<DiskEncryptionStatus> result = await service.GetStatusAsync(
            ScanTarget.Local, ScanCredentials.CurrentUser, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.WmiUnavailable, result.Error!.Code);
    }

    [Fact]
    public async Task RemoteTarget_SkipsLocalElevationCheck()
    {
        SetUpVolumes(new DiskEncryptionVolume("C:", DiskEncryptionProtectionStatus.Protected));
        DiskEncryptionService service = CreateService();

        Result<DiskEncryptionStatus> result = await service.GetStatusAsync(
            ScanTarget.Remote("pc-042"), ScanCredentials.CurrentUser, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("pc-042", result.Value.Host);
        await _reader.Received(1).ReadAsync(
            ScanTarget.Remote("pc-042"),
            ScanCredentials.CurrentUser,
            Arg.Any<ConnectionOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Elevated_MapsProtectionStatusPerVolume()
    {
        SetUpVolumes(
            new DiskEncryptionVolume("C:", DiskEncryptionProtectionStatus.Protected),
            new DiskEncryptionVolume("D:", DiskEncryptionProtectionStatus.Unprotected),
            new DiskEncryptionVolume(null, DiskEncryptionProtectionStatus.Unknown));
        DiskEncryptionService service = CreateService();

        Result<DiskEncryptionStatus> result = await service.GetStatusAsync(
            ScanTarget.Local, ScanCredentials.CurrentUser, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Environment.MachineName, result.Value.Host);
        Assert.Collection(
            result.Value.Volumes,
            volume => Assert.Equal(("C:", VolumeProtectionStatus.Protected), (volume.DriveLetter, volume.ProtectionStatus)),
            volume => Assert.Equal(("D:", VolumeProtectionStatus.Unprotected), (volume.DriveLetter, volume.ProtectionStatus)),
            volume => Assert.Equal((null, VolumeProtectionStatus.Unknown), (volume.DriveLetter, volume.ProtectionStatus)));
    }
}
