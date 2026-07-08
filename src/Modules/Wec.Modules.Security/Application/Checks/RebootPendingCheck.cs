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

    public async Task<IReadOnlyList<SecurityFinding>> EvaluateAsync(
        SecurityScanContext context,
        CancellationToken cancellationToken)
    {
        DateTimeOffset capturedAtUtc = _clock.UtcNow;

        var reasons = new List<string>();

        if (await HasSubKeyAsync(context, ComponentBasedServicingKey, "RebootPending", cancellationToken))
        {
            reasons.Add("Component Based Servicing: RebootPending");
        }

        if (await HasSubKeyAsync(context, AutoUpdateKey, "RebootRequired", cancellationToken))
        {
            reasons.Add("Windows Update: RebootRequired");
        }

        Result<object?> pendingRenames = await _registryReader.ReadLocalMachineValueAsync(
            context.Target, context.Credentials, context.Connection, SessionManagerKey, "PendingFileRenameOperations", cancellationToken);
        if (pendingRenames.IsSuccess && pendingRenames.Value is string[] { Length: > 0 })
        {
            reasons.Add("Session Manager: PendingFileRenameOperations");
        }

        if (reasons.Count == 0)
        {
            return [];
        }

        return [new SecurityFinding(
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
            capturedAtUtc)];
    }

    private async Task<bool> HasSubKeyAsync(
        SecurityScanContext context, string parentKeyPath, string subKeyName, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<string>> subKeys = await _registryReader.ReadLocalMachineSubKeyNamesAsync(
            context.Target, context.Credentials, context.Connection, parentKeyPath, cancellationToken);
        return subKeys.IsSuccess
            && subKeys.Value.Contains(subKeyName, StringComparer.OrdinalIgnoreCase);
    }
}
