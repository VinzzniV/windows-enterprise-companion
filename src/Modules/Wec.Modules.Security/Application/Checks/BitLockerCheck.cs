using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Security.Domain;

namespace Wec.Modules.Security.Application.Checks;

internal sealed class BitLockerCheck : ISecurityCheck
{
    private readonly IDiskEncryptionStatusReader _reader;
    private readonly IClock _clock;

    public BitLockerCheck(IDiskEncryptionStatusReader reader, IClock clock)
    {
        _reader = reader;
        _clock = clock;
    }

    public string CheckId => "WEC-SEC-BITLOCKER";

    public async Task<SecurityCheckResult> EvaluateAsync(
        SecurityScanContext context,
        CancellationToken cancellationToken)
    {
        DateTimeOffset capturedAtUtc = _clock.UtcNow;

        Result<IReadOnlyList<DiskEncryptionVolume>> volumes = await _reader.ReadAsync(
            context.Target,
            context.Credentials,
            context.Connection,
            cancellationToken);

        if (volumes.IsFailure)
        {
            return CheckFindings.NotRun(CheckId, volumes.Error!);
        }

        if (volumes.Value.Count == 0)
        {
            return CheckFindings.NotRun(CheckId, new Error(
                ErrorCode.NotFound,
                "No BitLocker volume state was returned; disk encryption could not be evaluated."));
        }

        var findings = new List<SecurityFinding>();
        foreach (DiskEncryptionVolume volume in volumes.Value)
        {
            if (volume.ProtectionStatus != DiskEncryptionProtectionStatus.Unprotected)
            {
                continue;
            }

            string driveLetter = volume.DriveLetter ?? "unknown";
            // MEDIUM, not HIGH: severity for an unencrypted volume was not fixed by
            // the product decision; per the agreed rule the conservative lower value
            // of the plausible MEDIUM-HIGH range is used.
            findings.Add(new SecurityFinding(
                $"{CheckId}-UNPROTECTED",
                $"Volume {driveLetter} is not protected by BitLocker",
                "The volume reports BitLocker protection as off. Data on it is readable if the disk "
                    + "is removed or the device is booted from external media.",
                FindingSeverity.Medium,
                FindingCategory.Encryption,
                $"Volume {driveLetter}",
                new Dictionary<string, string>
                {
                    ["driveLetter"] = driveLetter,
                    ["protectionStatus"] = "0 (unprotected)",
                    ["source"] = @"root\cimv2\Security\MicrosoftVolumeEncryption\Win32_EncryptableVolume",
                },
                "Enable BitLocker for this volume, or document why the device does not require "
                    + "disk encryption (e.g. stationary machine in a secured room).",
                RequiredPrivilege: null,
                capturedAtUtc));
        }

        if (volumes.Value.Any(volume => volume.ProtectionStatus == DiskEncryptionProtectionStatus.Unknown))
        {
            return SecurityCheckResult.DidNotRun(
                CheckId,
                new Error(
                    ErrorCode.WmiUnavailable,
                    "At least one BitLocker volume returned an unknown protection state; coverage is incomplete."),
                findings);
        }

        return SecurityCheckResult.Succeeded(CheckId, findings);
    }
}
