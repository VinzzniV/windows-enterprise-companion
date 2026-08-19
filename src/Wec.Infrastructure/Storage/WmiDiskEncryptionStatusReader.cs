using Wec.Core.Abstractions;
using Wec.Core.Privileges;
using Wec.Core.Results;
using Wec.Core.Targets;

namespace Wec.Infrastructure.Storage;

public sealed class WmiDiskEncryptionStatusReader : IDiskEncryptionStatusReader
{
    internal const string VolumeEncryptionNamespace = @"root\cimv2\Security\MicrosoftVolumeEncryption";
    internal const string VolumeQuery = "SELECT DriveLetter, ProtectionStatus FROM Win32_EncryptableVolume";

    private readonly IWmiQueryService _wmiQueryService;
    private readonly IPrivilegeContext _privilegeContext;

    public WmiDiskEncryptionStatusReader(
        IWmiQueryService wmiQueryService,
        IPrivilegeContext privilegeContext)
    {
        _wmiQueryService = wmiQueryService;
        _privilegeContext = privilegeContext;
    }

    public async Task<Result<IReadOnlyList<DiskEncryptionVolume>>> ReadAsync(
        ScanTarget target,
        ScanCredentials credentials,
        ConnectionOptions connection,
        CancellationToken cancellationToken)
    {
        if (target.IsLocal && !_privilegeContext.Satisfies(PrivilegeLevel.Administrator))
        {
            return Result.Failure<IReadOnlyList<DiskEncryptionVolume>>(Error.AccessDenied(
                "Reading BitLocker status requires administrator privileges.",
                PrivilegeLevel.Administrator));
        }

        Result<IReadOnlyList<WmiInstance>> result = await _wmiQueryService.QueryAsync(
            target,
            credentials,
            connection,
            VolumeEncryptionNamespace,
            VolumeQuery,
            cancellationToken);
        if (result.IsFailure)
        {
            return Result.Failure<IReadOnlyList<DiskEncryptionVolume>>(result.Error!);
        }

        return Result.Success<IReadOnlyList<DiskEncryptionVolume>>(
            result.Value.Select(Map).ToList());
    }

    private static DiskEncryptionVolume Map(WmiInstance instance) => new(
        instance.GetString("DriveLetter"),
        instance.GetInteger("ProtectionStatus") switch
        {
            0 => DiskEncryptionProtectionStatus.Unprotected,
            1 => DiskEncryptionProtectionStatus.Protected,
            _ => DiskEncryptionProtectionStatus.Unknown,
        });
}
