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

    public Task<IReadOnlyList<SecurityFinding>> EvaluateAsync(
        SecurityScanContext context,
        CancellationToken cancellationToken)
    {
        DateTimeOffset capturedAtUtc = _clock.UtcNow;

        if (!context.Target.IsLocal)
        {
            return Task.FromResult<IReadOnlyList<SecurityFinding>>([CheckFindings.LocalOnly(
                CheckId,
                "UAC state was not checked on the remote target",
                FindingCategory.Accounts,
                "User Account Control",
                context.Target.DisplayName,
                capturedAtUtc)]);
        }

        Result<object?> enableLua = _registryReader.ReadLocalMachineValue(PoliciesSystemKey, "EnableLUA");
        if (enableLua.IsFailure)
        {
            return Task.FromResult<IReadOnlyList<SecurityFinding>>([CheckFindings.NotRun(
                CheckId,
                "UAC state could not be determined",
                FindingCategory.Accounts,
                "User Account Control",
                "Verify registry read permissions and retry the scan.",
                enableLua.Error!,
                capturedAtUtc)]);
        }

        // Missing value = Windows default (UAC on) — no finding
        if (enableLua.Value is int enabled && enabled == 0)
        {
            return Task.FromResult<IReadOnlyList<SecurityFinding>>([new SecurityFinding(
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

        Result<object?> consentBehavior =
            _registryReader.ReadLocalMachineValue(PoliciesSystemKey, "ConsentPromptBehaviorAdmin");
        if (consentBehavior.IsSuccess && consentBehavior.Value is int behavior && behavior == 0)
        {
            return Task.FromResult<IReadOnlyList<SecurityFinding>>([new SecurityFinding(
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

        return Task.FromResult<IReadOnlyList<SecurityFinding>>([]);
    }
}
