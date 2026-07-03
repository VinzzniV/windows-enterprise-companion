using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Privileges;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.Inventory.Application;
using Wec.Modules.Inventory.Domain;

namespace Wec.Modules.Inventory.Tests.Application;

public class DiskEncryptionServiceTests
{
    private readonly IWmiQueryService _wmiQueryService = Substitute.For<IWmiQueryService>();
    private readonly IPrivilegeContext _privilegeContext = Substitute.For<IPrivilegeContext>();

    private DiskEncryptionService CreateService() => new(
        _wmiQueryService,
        _privilegeContext,
        Microsoft.Extensions.Options.Options.Create(new RemoteScanOptions()),
        NullLogger<DiskEncryptionService>.Instance);

    private void SetUpVolumes(params WmiInstance[] volumes) =>
        _wmiQueryService
            .QueryAsync(
                Arg.Any<ScanTarget>(),
                Arg.Any<ScanCredentials>(),
                Arg.Any<ConnectionOptions>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<WmiInstance>>(volumes));

    [Fact]
    public async Task Unelevated_ReturnsAccessDeniedWithRequiredPrivilege_WithoutQueryingWmi()
    {
        _privilegeContext.Satisfies(PrivilegeLevel.Administrator).Returns(false);
        DiskEncryptionService service = CreateService();

        Result<DiskEncryptionStatus> result = await service.GetStatusAsync(
            ScanTarget.Local, ScanCredentials.CurrentUser, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.AccessDenied, result.Error!.Code);
        Assert.Equal(PrivilegeLevel.Administrator, result.Error.RequiredPrivilege);
        await _wmiQueryService.DidNotReceive().QueryAsync(
            Arg.Any<ScanTarget>(),
            Arg.Any<ScanCredentials>(),
            Arg.Any<ConnectionOptions>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoteTarget_SkipsLocalElevationCheck()
    {
        _privilegeContext.Satisfies(PrivilegeLevel.Administrator).Returns(false);
        SetUpVolumes(new WmiInstance(new Dictionary<string, object?>
        {
            ["DriveLetter"] = "C:",
            ["ProtectionStatus"] = 1u,
        }));
        DiskEncryptionService service = CreateService();

        Result<DiskEncryptionStatus> result = await service.GetStatusAsync(
            ScanTarget.Remote("pc-042"), ScanCredentials.CurrentUser, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("pc-042", result.Value.Host);
    }

    [Fact]
    public async Task Elevated_MapsProtectionStatusPerVolume()
    {
        _privilegeContext.Satisfies(PrivilegeLevel.Administrator).Returns(true);
        SetUpVolumes(
            new WmiInstance(new Dictionary<string, object?>
            {
                ["DriveLetter"] = "C:",
                ["ProtectionStatus"] = 1u,
            }),
            new WmiInstance(new Dictionary<string, object?>
            {
                ["DriveLetter"] = "D:",
                ["ProtectionStatus"] = 0u,
            }));
        DiskEncryptionService service = CreateService();

        Result<DiskEncryptionStatus> result = await service.GetStatusAsync(
            ScanTarget.Local, ScanCredentials.CurrentUser, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Environment.MachineName, result.Value.Host);
        Assert.Collection(
            result.Value.Volumes,
            volume => Assert.Equal(("C:", VolumeProtectionStatus.Protected), (volume.DriveLetter, volume.ProtectionStatus)),
            volume => Assert.Equal(("D:", VolumeProtectionStatus.Unprotected), (volume.DriveLetter, volume.ProtectionStatus)));
    }
}
