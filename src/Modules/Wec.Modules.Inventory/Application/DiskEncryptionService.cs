using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Privileges;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.Inventory.Domain;

namespace Wec.Modules.Inventory.Application;

public sealed record DiskEncryptionStatus(string Host, IReadOnlyList<EncryptableVolume> Volumes);

public sealed class DiskEncryptionService
{
    private const string VolumeEncryptionNamespace = @"root\cimv2\Security\MicrosoftVolumeEncryption";

    private readonly IWmiQueryService _wmiQueryService;
    private readonly IPrivilegeContext _privilegeContext;
    private readonly ConnectionOptions _connectionOptions;
    private readonly ILogger<DiskEncryptionService> _logger;

    public DiskEncryptionService(
        IWmiQueryService wmiQueryService,
        IPrivilegeContext privilegeContext,
        IOptions<RemoteScanOptions> remoteScanOptions,
        ILogger<DiskEncryptionService> logger)
    {
        _wmiQueryService = wmiQueryService;
        _privilegeContext = privilegeContext;
        _connectionOptions = remoteScanOptions.Value.ToConnectionOptions();
        _logger = logger;
    }

    public async Task<Result<DiskEncryptionStatus>> GetStatusAsync(
        ScanTarget target,
        ScanCredentials credentials,
        CancellationToken cancellationToken)
    {
        // Declared privilege requirement (ADR 0002): fail deterministically before
        // touching WMI instead of depending on the provider's access-denied behavior.
        // Remote rights come from the connection credentials, not this process.
        if (target.IsLocal && !_privilegeContext.Satisfies(PrivilegeLevel.Administrator))
        {
            _logger.LogInformation("Disk encryption status requested without elevation");
            return Result.Failure<DiskEncryptionStatus>(Error.AccessDenied(
                "Reading BitLocker status requires administrator privileges.",
                PrivilegeLevel.Administrator));
        }

        Result<IReadOnlyList<WmiInstance>> volumes = await _wmiQueryService.QueryAsync(
            target,
            credentials,
            _connectionOptions,
            VolumeEncryptionNamespace,
            "SELECT DriveLetter, ProtectionStatus FROM Win32_EncryptableVolume",
            cancellationToken);
        if (volumes.IsFailure)
        {
            return Result.Failure<DiskEncryptionStatus>(volumes.Error!);
        }

        return Result.Success(new DiskEncryptionStatus(
            target.DisplayName,
            volumes.Value.Select(ToEncryptableVolume).ToList()));
    }

    private static EncryptableVolume ToEncryptableVolume(WmiInstance instance) => new(
        instance.GetString("DriveLetter"),
        (instance.GetInteger("ProtectionStatus") ?? 2) switch
        {
            0 => VolumeProtectionStatus.Unprotected,
            1 => VolumeProtectionStatus.Protected,
            _ => VolumeProtectionStatus.Unknown,
        });
}
