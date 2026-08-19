using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Security.Domain;

namespace Wec.Modules.Security.Application.Checks;

internal sealed class UacCheck : ISecurityCheck
{
    private const string PoliciesSystemKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";

    private readonly IRegistryReader _registryReader;
    private readonly IClock _clock;

    public UacCheck(IRegistryReader registryReader, IClock clock)
    {
        _registryReader = registryReader;
        _clock = clock;
    }

    public string CheckId => "WEC-SEC-UAC";

    public async Task<SecurityCheckResult> EvaluateAsync(
        SecurityScanContext context,
        CancellationToken cancellationToken)
    {
        DateTimeOffset capturedAtUtc = _clock.UtcNow;

        Result<object?> enableLua = await _registryReader.ReadLocalMachineValueAsync(
            context.Target, context.Credentials, context.Connection, PoliciesSystemKey, "EnableLUA", cancellationToken);
        if (enableLua.IsFailure)
        {
            return CheckFindings.NotRun(CheckId, enableLua.Error!);
        }

        // Missing value = Windows default (UAC on) — no finding
        if (enableLua.Value is int enabled && enabled == 0)
        {
            return SecurityCheckResult.Succeeded(CheckId, [new SecurityFinding(
                $"{CheckId}-DISABLED",
                "User Account Control is disabled",
                "EnableLUA is 0: every process started by an administrator account runs with full "
                    + "administrative rights, without any elevation prompt or token split.",
                FindingSeverity.High,
                FindingCategory.Accounts,
                "User Account Control",
                new Dictionary<string, string>
                {
                    ["registryKey"] = $@"HKLM\{PoliciesSystemKey}",
                    ["enableLUA"] = "0",
                },
                "Re-enable UAC (EnableLUA = 1) and reboot. Software that 'requires' disabled UAC "
                    + "should be treated as a legacy exception and isolated.",
                RequiredPrivilege: null,
                capturedAtUtc)]);
        }

        Result<object?> consentBehavior = await _registryReader.ReadLocalMachineValueAsync(
            context.Target, context.Credentials, context.Connection, PoliciesSystemKey, "ConsentPromptBehaviorAdmin", cancellationToken);
        if (consentBehavior.IsFailure)
        {
            return CheckFindings.NotRun(CheckId, consentBehavior.Error!);
        }

        if (consentBehavior.Value is int behavior && behavior == 0)
        {
            return SecurityCheckResult.Succeeded(CheckId, [new SecurityFinding(
                $"{CheckId}-SILENT-ELEVATION",
                "UAC elevates administrators without prompting",
                "ConsentPromptBehaviorAdmin is 0: elevation happens silently. Malware running in an "
                    + "administrator session can elevate without any user interaction.",
                FindingSeverity.Medium,
                FindingCategory.Accounts,
                "User Account Control",
                new Dictionary<string, string>
                {
                    ["registryKey"] = $@"HKLM\{PoliciesSystemKey}",
                    ["consentPromptBehaviorAdmin"] = "0",
                },
                "Set ConsentPromptBehaviorAdmin to prompt for consent on the secure desktop "
                    + "(value 2 or the Windows default 5).",
                RequiredPrivilege: null,
                capturedAtUtc)]);
        }

        return SecurityCheckResult.Succeeded(CheckId);
    }
}
