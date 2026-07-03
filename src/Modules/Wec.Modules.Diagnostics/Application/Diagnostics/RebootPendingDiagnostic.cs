using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Diagnostics.Domain;

namespace Wec.Modules.Diagnostics.Application.Diagnostics;

internal sealed class RebootPendingDiagnostic : IDiagnostic
{
    private const string ComponentBasedServicingKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing";
    private const string AutoUpdateKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update";
    private const string SessionManagerKey = @"SYSTEM\CurrentControlSet\Control\Session Manager";

    private readonly IRegistryReader _registryReader;
    private readonly IClock _clock;

    public RebootPendingDiagnostic(IRegistryReader registryReader, IClock clock)
    {
        _registryReader = registryReader;
        _clock = clock;
    }

    public string DiagnosticId => "WEC-DIAG-SYS-REBOOT";

    public Task<IReadOnlyList<DiagnosticResult>> EvaluateAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset capturedAtUtc = _clock.UtcNow;
        var signals = new List<string>();

        if (HasSubKey(ComponentBasedServicingKey, "RebootPending"))
        {
            signals.Add("Component Based Servicing: RebootPending");
        }

        if (HasSubKey(AutoUpdateKey, "RebootRequired"))
        {
            signals.Add("Windows Update: RebootRequired");
        }

        Result<object?> pendingRenames =
            _registryReader.ReadLocalMachineValue(SessionManagerKey, "PendingFileRenameOperations");
        if (pendingRenames.IsSuccess && pendingRenames.Value is string[] { Length: > 0 })
        {
            signals.Add("Session Manager: PendingFileRenameOperations");
        }

        if (signals.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<DiagnosticResult>>([BuildResult(
                DiagnosticStatus.Pass,
                "No reboot is pending",
                new Dictionary<string, string>(),
                [],
                capturedAtUtc)]);
        }

        return Task.FromResult<IReadOnlyList<DiagnosticResult>>([BuildResult(
            DiagnosticStatus.Warning,
            "A reboot is pending",
            new Dictionary<string, string> { ["signals"] = string.Join("; ", signals) },
            ["Reboot the machine so pending updates and component changes become effective."],
            capturedAtUtc)]);
    }

    private bool HasSubKey(string parentKeyPath, string subKeyName)
    {
        Result<IReadOnlyList<string>> subKeys = _registryReader.ReadLocalMachineSubKeyNames(parentKeyPath);
        return subKeys.IsSuccess
            && subKeys.Value.Contains(subKeyName, StringComparer.OrdinalIgnoreCase);
    }

    private DiagnosticResult BuildResult(
        DiagnosticStatus status,
        string title,
        IReadOnlyDictionary<string, string> evidence,
        IReadOnlyList<string> nextSteps,
        DateTimeOffset capturedAtUtc) => new(
        DiagnosticId,
        title,
        status,
        DiagnosticCategory.System,
        "Pending reboot",
        evidence,
        nextSteps,
        RequiredPrivilege: null,
        capturedAtUtc);
}
