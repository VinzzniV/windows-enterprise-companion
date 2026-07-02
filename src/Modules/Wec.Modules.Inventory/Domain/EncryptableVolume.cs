namespace Wec.Modules.Inventory.Domain;

public enum VolumeProtectionStatus
{
    Unprotected = 0,
    Protected = 1,
    Unknown = 2,
}

public sealed record EncryptableVolume(string? DriveLetter, VolumeProtectionStatus ProtectionStatus);
