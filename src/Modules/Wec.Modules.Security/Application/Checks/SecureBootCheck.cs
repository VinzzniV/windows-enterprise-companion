using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Security.Domain;

namespace Wec.Modules.Security.Application.Checks;

internal sealed class SecureBootCheck : ISecurityCheck
{
    private const string SecureBootStateKey = @"SYSTEM\CurrentControlSet\Control\SecureBoot\State";
    private const string SecureBootEnabledValue = "UEFISecureBootEnabled";

    private readonly IRegistryReader _registryReader;
    private readonly IClock _clock;

    public SecureBootCheck(IRegistryReader registryReader, IClock clock)
    {
        _registryReader = registryReader;
        _clock = clock;
    }

    public string CheckId => "WEC-SEC-SECUREBOOT";

    public async Task<SecurityCheckResult> EvaluateAsync(
        SecurityScanContext context,
        CancellationToken cancellationToken)
    {
        DateTimeOffset capturedAtUtc = _clock.UtcNow;

        Result<object?> state = await _registryReader.ReadLocalMachineValueAsync(
            context.Target, context.Credentials, context.Connection, SecureBootStateKey, SecureBootEnabledValue, cancellationToken);

        if (state.IsFailure)
        {
            return CheckFindings.NotRun(CheckId, state.Error!);
        }

        IReadOnlyList<SecurityFinding> findings = state.Value switch
        {
            int enabled when enabled == 1 => [],
            int => [DisabledFinding(capturedAtUtc)],
            _ => [NotAvailableFinding(capturedAtUtc)],
        };
        return SecurityCheckResult.Succeeded(CheckId, findings);
    }

    private SecurityFinding DisabledFinding(DateTimeOffset capturedAtUtc) => new(
        $"{CheckId}-DISABLED",
        "Secure Boot is disabled",
        "The firmware reports Secure Boot as turned off. Boot-time malware (bootkits) can load "
            + "before the operating system's own protections start.",
        FindingSeverity.Medium,
        FindingCategory.PlatformIntegrity,
        "Secure Boot",
        new Dictionary<string, string>
        {
            ["registryKey"] = $@"HKLM\{SecureBootStateKey}",
            ["uefiSecureBootEnabled"] = "0",
        },
        "Enable Secure Boot in the UEFI firmware settings unless an incompatible component "
            + "prevents it; document any exception.",
        RequiredPrivilege: null,
        capturedAtUtc);

    // LOW, not MEDIUM: a missing state key usually means legacy BIOS or a VM without
    // UEFI — plausible range LOW-MEDIUM, the conservative lower value is used.
    private SecurityFinding NotAvailableFinding(DateTimeOffset capturedAtUtc) => new(
        $"{CheckId}-UNAVAILABLE",
        "Secure Boot is not available on this platform",
        "No Secure Boot state is exposed. The machine likely boots via legacy BIOS or runs in a "
            + "virtualized environment without UEFI Secure Boot.",
        FindingSeverity.Low,
        FindingCategory.PlatformIntegrity,
        "Secure Boot",
        new Dictionary<string, string>
        {
            ["registryKey"] = $@"HKLM\{SecureBootStateKey}",
            ["uefiSecureBootEnabled"] = "missing",
        },
        "If the hardware supports UEFI, switch the firmware from legacy/CSM boot to UEFI with "
            + "Secure Boot. On virtual machines, prefer generation/firmware settings that support it.",
        RequiredPrivilege: null,
        capturedAtUtc);
}
