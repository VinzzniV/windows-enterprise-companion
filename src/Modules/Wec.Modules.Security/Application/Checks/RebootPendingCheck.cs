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

    public async Task<SecurityCheckResult> EvaluateAsync(
        SecurityScanContext context,
        CancellationToken cancellationToken)
    {
        DateTimeOffset capturedAtUtc = _clock.UtcNow;

        var reasons = new List<string>();

        Result<bool> componentBasedServicing = await HasSubKeyAsync(
            context, ComponentBasedServicingKey, "RebootPending", cancellationToken);
        if (componentBasedServicing.IsFailure)
        {
            return CheckFindings.NotRun(CheckId, componentBasedServicing.Error!);
        }

        if (componentBasedServicing.Value)
        {
            reasons.Add("Component Based Servicing: RebootPending");
        }

        Result<bool> windowsUpdate = await HasSubKeyAsync(
            context, AutoUpdateKey, "RebootRequired", cancellationToken);
        if (windowsUpdate.IsFailure)
        {
            return CheckFindings.NotRun(CheckId, windowsUpdate.Error!);
        }

        if (windowsUpdate.Value)
        {
            reasons.Add("Windows Update: RebootRequired");
        }

        Result<object?> pendingRenames = await _registryReader.ReadLocalMachineValueAsync(
            context.Target, context.Credentials, context.Connection, SessionManagerKey, "PendingFileRenameOperations", cancellationToken);
        if (pendingRenames.IsFailure)
        {
            return CheckFindings.NotRun(CheckId, pendingRenames.Error!);
        }

        if (pendingRenames.Value is string[] { Length: > 0 })
        {
            reasons.Add("Session Manager: PendingFileRenameOperations");
        }

        if (reasons.Count == 0)
        {
            return SecurityCheckResult.Succeeded(CheckId);
        }

        return SecurityCheckResult.Succeeded(CheckId, [new SecurityFinding(
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

    private async Task<Result<bool>> HasSubKeyAsync(
        SecurityScanContext context, string parentKeyPath, string subKeyName, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<string>> subKeys = await _registryReader.ReadLocalMachineSubKeyNamesAsync(
            context.Target, context.Credentials, context.Connection, parentKeyPath, cancellationToken);
        return subKeys.IsFailure
            ? Result.Failure<bool>(subKeys.Error!)
            : Result.Success(subKeys.Value.Contains(subKeyName, StringComparer.OrdinalIgnoreCase));
    }
}
