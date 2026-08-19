using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.Inventory.Domain;

namespace Wec.Modules.Inventory.Application;

public sealed record DiskEncryptionStatus(string Host, IReadOnlyList<EncryptableVolume> Volumes);

public sealed class DiskEncryptionService
{
    private readonly IDiskEncryptionStatusReader _reader;
    private readonly ConnectionOptions _connectionOptions;

    public DiskEncryptionService(
        IDiskEncryptionStatusReader reader,
        IOptions<RemoteScanOptions> remoteScanOptions)
    {
        _reader = reader;
        _connectionOptions = remoteScanOptions.Value.ToConnectionOptions();
    }

    public async Task<Result<DiskEncryptionStatus>> GetStatusAsync(
        ScanTarget target,
        ScanCredentials credentials,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<DiskEncryptionVolume>> volumes = await _reader.ReadAsync(
            target,
            credentials,
            _connectionOptions,
            cancellationToken);
        if (volumes.IsFailure)
        {
            return Result.Failure<DiskEncryptionStatus>(volumes.Error!);
        }

        return Result.Success(new DiskEncryptionStatus(
            target.DisplayName,
            volumes.Value.Select(ToEncryptableVolume).ToList()));
    }

    private static EncryptableVolume ToEncryptableVolume(DiskEncryptionVolume volume) => new(
        volume.DriveLetter,
        volume.ProtectionStatus switch
        {
            DiskEncryptionProtectionStatus.Unprotected => VolumeProtectionStatus.Unprotected,
            DiskEncryptionProtectionStatus.Protected => VolumeProtectionStatus.Protected,
            _ => VolumeProtectionStatus.Unknown,
        });
}
