using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Privileges;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Infrastructure.Storage;

namespace Wec.Infrastructure.IntegrationTests.Storage;

public sealed class WmiDiskEncryptionStatusReaderTests
{
    private readonly IWmiQueryService _wmi = Substitute.For<IWmiQueryService>();
    private readonly IPrivilegeContext _privilege = Substitute.For<IPrivilegeContext>();

    [Fact]
    public async Task LocalWithoutElevation_FailsBeforeQueryingProvider()
    {
        _privilege.Satisfies(PrivilegeLevel.Administrator).Returns(false);
        var reader = new WmiDiskEncryptionStatusReader(_wmi, _privilege);

        Result<IReadOnlyList<DiskEncryptionVolume>> result = await reader.ReadAsync(
            ScanTarget.Local,
            ScanCredentials.CurrentUser,
            ConnectionOptions.Default,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.AccessDenied, result.Error!.Code);
        Assert.Equal(PrivilegeLevel.Administrator, result.Error.RequiredPrivilege);
        await _wmi.DidNotReceiveWithAnyArgs().QueryAsync(
            default!, default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task RemoteRead_UsesProvidedContextAndMapsEveryProviderState()
    {
        _privilege.Satisfies(PrivilegeLevel.Administrator).Returns(false);
        ScanTarget target = ScanTarget.Remote("pc-42");
        ScanCredentials credentials = ScanCredentials.Explicit("operator", "CONTOSO", "secret");
        var connection = new ConnectionOptions { Timeout = TimeSpan.FromSeconds(17) };
        _wmi.QueryAsync(
                target,
                credentials,
                connection,
                @"root\cimv2\Security\MicrosoftVolumeEncryption",
                "SELECT DriveLetter, ProtectionStatus FROM Win32_EncryptableVolume",
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<WmiInstance>>(
            [
                Volume("C:", 0u),
                Volume("D:", 1u),
                Volume("E:", 2u),
                Volume("F:", null),
                Volume("G:", 99u),
            ]));
        var reader = new WmiDiskEncryptionStatusReader(_wmi, _privilege);

        Result<IReadOnlyList<DiskEncryptionVolume>> result = await reader.ReadAsync(
            target, credentials, connection, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Collection(
            result.Value,
            volume => Assert.Equal(DiskEncryptionProtectionStatus.Unprotected, volume.ProtectionStatus),
            volume => Assert.Equal(DiskEncryptionProtectionStatus.Protected, volume.ProtectionStatus),
            volume => Assert.Equal(DiskEncryptionProtectionStatus.Unknown, volume.ProtectionStatus),
            volume => Assert.Equal(DiskEncryptionProtectionStatus.Unknown, volume.ProtectionStatus),
            volume => Assert.Equal(DiskEncryptionProtectionStatus.Unknown, volume.ProtectionStatus));
    }

    [Fact]
    public async Task ProviderFailure_IsForwardedUnchanged()
    {
        _privilege.Satisfies(PrivilegeLevel.Administrator).Returns(true);
        Error expected = Error.WmiUnavailable("provider unavailable");
        _wmi.QueryAsync(
                Arg.Any<ScanTarget>(),
                Arg.Any<ScanCredentials>(),
                Arg.Any<ConnectionOptions>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<WmiInstance>>(expected));
        var reader = new WmiDiskEncryptionStatusReader(_wmi, _privilege);

        Result<IReadOnlyList<DiskEncryptionVolume>> result = await reader.ReadAsync(
            ScanTarget.Local,
            ScanCredentials.CurrentUser,
            ConnectionOptions.Default,
            CancellationToken.None);

        Assert.Same(expected, result.Error);
    }

    private static WmiInstance Volume(string driveLetter, uint? protectionStatus) => new(
        new Dictionary<string, object?>
        {
            ["DriveLetter"] = driveLetter,
            ["ProtectionStatus"] = protectionStatus,
        });
}
