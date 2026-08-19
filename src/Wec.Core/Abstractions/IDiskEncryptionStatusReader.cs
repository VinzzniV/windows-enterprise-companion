using Wec.Core.Results;
using Wec.Core.Targets;

namespace Wec.Core.Abstractions;

public enum DiskEncryptionProtectionStatus
{
    Unprotected,
    Protected,
    Unknown,
}

public sealed record DiskEncryptionVolume(
    string? DriveLetter,
    DiskEncryptionProtectionStatus ProtectionStatus);

/// <summary>
/// Narrow shared provider for the BitLocker state consumed by Inventory and
/// Security. It deliberately exposes no general-purpose system-facts model.
/// </summary>
public interface IDiskEncryptionStatusReader
{
    Task<Result<IReadOnlyList<DiskEncryptionVolume>>> ReadAsync(
        ScanTarget target,
        ScanCredentials credentials,
        ConnectionOptions connection,
        CancellationToken cancellationToken);
}
