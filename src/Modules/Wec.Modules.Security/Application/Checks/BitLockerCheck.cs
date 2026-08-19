using Wec.Core.Abstractions;
using Wec.Core.Privileges;
using Wec.Core.Results;
using Wec.Modules.Security.Domain;

namespace Wec.Modules.Security.Application.Checks;

internal sealed class BitLockerCheck : ISecurityCheck
{
    private const string VolumeEncryptionNamespace = @"root\cimv2\Security\MicrosoftVolumeEncryption";
    private const int ProtectionStatusUnprotected = 0;

    private readonly IWmiQueryService _wmiQueryService;
    private readonly IPrivilegeContext _privilegeContext;
    private readonly IClock _clock;

    public BitLockerCheck(IWmiQueryService wmiQueryService, IPrivilegeContext privilegeContext, IClock clock)
    {
        _wmiQueryService = wmiQueryService;
        _privilegeContext = privilegeContext;
        _clock = clock;
    }

    public string CheckId => "WEC-SEC-BITLOCKER";

    public async Task<SecurityCheckResult> EvaluateAsync(
        SecurityScanContext context,
        CancellationToken cancellationToken)
    {
        DateTimeOffset capturedAtUtc = _clock.UtcNow;

        // Same elevation-aware semantics as the inventory BitLocker card (ADR 0002):
        // fail deterministically before touching WMI. INFO severity per the agreed
        // rule — a check blocked by missing rights is reported, not alarmed.
        // Remote rights come from the connection credentials, not this process.
        if (context.Target.IsLocal && !_privilegeContext.Satisfies(PrivilegeLevel.Administrator))
        {
            return CheckFindings.NotRun(
                CheckId,
                Error.AccessDenied(
                    "Reading BitLocker status requires administrator privileges.",
                    PrivilegeLevel.Administrator));
        }

        Result<IReadOnlyList<WmiInstance>> volumes = await _wmiQueryService.QueryAsync(
            context,
            VolumeEncryptionNamespace,
            "SELECT DriveLetter, ProtectionStatus FROM Win32_EncryptableVolume",
            cancellationToken);

        if (volumes.IsFailure)
        {
            return CheckFindings.NotRun(CheckId, volumes.Error!);
        }

        var findings = new List<SecurityFinding>();
        foreach (WmiInstance volume in volumes.Value)
        {
            if (volume.GetInteger("ProtectionStatus") != ProtectionStatusUnprotected)
            {
                continue;
            }

            string driveLetter = volume.GetString("DriveLetter") ?? "unknown";
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
                    ["source"] = $@"{VolumeEncryptionNamespace}\Win32_EncryptableVolume",
                },
                "Enable BitLocker for this volume, or document why the device does not require "
                    + "disk encryption (e.g. stationary machine in a secured room).",
                RequiredPrivilege: null,
                capturedAtUtc));
        }

        return SecurityCheckResult.Succeeded(CheckId, findings);
    }
}
