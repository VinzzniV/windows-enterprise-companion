using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Security.Domain;

namespace Wec.Modules.Security.Application.Checks;

internal sealed class TpmCheck : ISecurityCheck
{
    private const string TpmNamespace = @"root\cimv2\Security\MicrosoftTpm";

    private readonly IWmiQueryService _wmiQueryService;
    private readonly IClock _clock;

    public TpmCheck(IWmiQueryService wmiQueryService, IClock clock)
    {
        _wmiQueryService = wmiQueryService;
        _clock = clock;
    }

    public string CheckId => "WEC-SEC-TPM";

    public async Task<IReadOnlyList<SecurityFinding>> EvaluateAsync(
        SecurityScanContext context,
        CancellationToken cancellationToken)
    {
        // Win32_Tpm requires elevation on most systems; the access-denied result
        // becomes a NOT-RUN finding carrying the required privilege.
        Result<IReadOnlyList<WmiInstance>> tpm = await _wmiQueryService.QueryAsync(
            context,
            TpmNamespace,
            "SELECT IsEnabled_InitialValue, IsActivated_InitialValue, SpecVersion FROM Win32_Tpm",
            cancellationToken);

        DateTimeOffset capturedAtUtc = _clock.UtcNow;

        if (tpm.IsFailure)
        {
            return [CheckFindings.NotRun(
                CheckId,
                "TPM state was not checked",
                FindingCategory.PlatformIntegrity,
                "Trusted Platform Module",
                "Restart the app as administrator to include the TPM in the scan.",
                tpm.Error!,
                capturedAtUtc)];
        }

        if (tpm.Value.Count == 0)
        {
            // LOW, not MEDIUM: no visible TPM is common on virtual machines and the
            // evidence cannot distinguish "absent" from "hidden by the hypervisor" —
            // conservative lower value of the plausible LOW-MEDIUM range.
            return [new SecurityFinding(
                $"{CheckId}-ABSENT",
                "No Trusted Platform Module is visible",
                "The system exposes no TPM device. Features like BitLocker key protection, "
                    + "measured boot and Windows Hello depend on a TPM.",
                FindingSeverity.Low,
                FindingCategory.PlatformIntegrity,
                "Trusted Platform Module",
                new Dictionary<string, string>
                {
                    ["tpmPresent"] = "false",
                    ["source"] = $@"{TpmNamespace}\Win32_Tpm",
                },
                "On physical machines, enable the TPM (or fTPM/PTT) in the firmware settings. "
                    + "On virtual machines, add a virtual TPM if the hypervisor supports it.",
                RequiredPrivilege: null,
                capturedAtUtc)];
        }

        WmiInstance tpmInstance = tpm.Value[0];
        if (tpmInstance.GetValue<bool?>("IsEnabled_InitialValue") == false)
        {
            // MEDIUM: a TPM exists but is turned off — stronger evidence of a
            // misconfiguration than a machine without any TPM.
            return [new SecurityFinding(
                $"{CheckId}-DISABLED",
                "The Trusted Platform Module is present but disabled",
                "A TPM device exists but reports itself as disabled. Security features that "
                    + "depend on it (BitLocker, measured boot) cannot use it.",
                FindingSeverity.Medium,
                FindingCategory.PlatformIntegrity,
                "Trusted Platform Module",
                new Dictionary<string, string>
                {
                    ["tpmPresent"] = "true",
                    ["isEnabled"] = "false",
                    ["specVersion"] = tpmInstance.GetString("SpecVersion") ?? "unknown",
                    ["source"] = $@"{TpmNamespace}\Win32_Tpm",
                },
                "Enable the TPM in the firmware settings.",
                RequiredPrivilege: null,
                capturedAtUtc)];
        }

        return [];
    }
}
