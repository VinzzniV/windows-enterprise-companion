using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Security.Domain;

namespace Wec.Modules.Security.Application.Checks;

internal sealed class RebootPendingCheck : ISecurityCheck
{
    private const string ComponentBasedServicingKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing";
    private const string AutoUpdateKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update";
    private const string SessionManagerKey = @"SYSTEM\CurrentControlSet\Control\Session Manager";

    private readonly IRegistryReader _registryReader;
    private readonly IClock _clock;

    public RebootPendingCheck(IRegistryReader registryReader, IClock clock)
    {
        _registryReader = registryReader;
        _clock = clock;
    }

    public string CheckId => "WEC-SEC-REBOOTPENDING";

    public Task<IReadOnlyList<SecurityFinding>> EvaluateAsync(
        SecurityScanContext context,
        CancellationToken cancellationToken)
    {
        DateTimeOffset capturedAtUtc = _clock.UtcNow;

        if (!context.Target.IsLocal)
        {
            return Task.FromResult<IReadOnlyList<SecurityFinding>>([CheckFindings.LocalOnly(
                CheckId,
                "Pending reboot state was not checked on the remote target",
                FindingCategory.OperatingSystem,
                "Pending reboot",
                context.Target.DisplayName,
                capturedAtUtc)]);
        }

        var reasons = new List<string>();

        if (HasSubKey(ComponentBasedServicingKey, "RebootPending"))
        {
            reasons.Add("Component Based Servicing: RebootPending");
        }

        if (HasSubKey(AutoUpdateKey, "RebootRequired"))
        {
            reasons.Add("Windows Update: RebootRequired");
        }

        Result<object?> pendingRenames =
            _registryReader.ReadLocalMachineValue(SessionManagerKey, "PendingFileRenameOperations");
        if (pendingRenames.IsSuccess && pendingRenames.Value is string[] { Length: > 0 })
        {
            reasons.Add("Session Manager: PendingFileRenameOperations");
        }

        if (reasons.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<SecurityFinding>>([]);
        }

        return Task.FromResult<IReadOnlyList<SecurityFinding>>([new SecurityFinding(
            $"{CheckId}-PENDING",
            "A reboot is pending",
            "Windows signals a pending reboot. Installed updates or component changes are not "
                + "fully effective until the machine restarts.",
            FindingSeverity.Info,
            FindingCategory.OperatingSystem,
            "Pending reboot",
            new Dictionary<string, string>
            {
                ["signals"] = string.Join("; ", reasons),
            },
            "Reboot the machine at the next opportunity so pending updates become effective.",
            RequiredPrivilege: null,
            capturedAtUtc)]);
    }

    private bool HasSubKey(string parentKeyPath, string subKeyName)
    {
        Result<IReadOnlyList<string>> subKeys = _registryReader.ReadLocalMachineSubKeyNames(parentKeyPath);
        return subKeys.IsSuccess
            && subKeys.Value.Contains(subKeyName, StringComparer.OrdinalIgnoreCase);
    }
}
