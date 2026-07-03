using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Diagnostics.Domain;

namespace Wec.Modules.Diagnostics.Application.Diagnostics;

/// <summary>
/// Pending-reboot signals from the registry: local reads use the registry
/// seam, remote targets are read through the WMI StdRegProv provider.
/// </summary>
internal sealed class RebootPendingDiagnostic : IDiagnostic
{
    private const string ComponentBasedServicingKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing";
    private const string AutoUpdateKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update";
    private const string SessionManagerKey = @"SYSTEM\CurrentControlSet\Control\Session Manager";
    private const string RegistryNamespace = @"root\default";
    private const string StdRegProvClass = "StdRegProv";
    private const uint HkeyLocalMachine = 0x80000002;

    private readonly IRegistryReader _registryReader;
    private readonly IWmiQueryService _wmiQueryService;
    private readonly IClock _clock;

    public RebootPendingDiagnostic(
        IRegistryReader registryReader,
        IWmiQueryService wmiQueryService,
        IClock clock)
    {
        _registryReader = registryReader;
        _wmiQueryService = wmiQueryService;
        _clock = clock;
    }

    public string DiagnosticId => "WEC-DIAG-SYS-REBOOT";

    public async Task<IReadOnlyList<DiagnosticResult>> EvaluateAsync(DiagnosticContext context, CancellationToken cancellationToken)
    {
        Result<List<string>> signals = context.Target.IsLocal
            ? CollectLocalSignals()
            : await CollectRemoteSignalsAsync(context, cancellationToken);
        DateTimeOffset capturedAtUtc = _clock.UtcNow;

        if (signals.IsFailure)
        {
            return [BuildResult(
                DiagnosticStatus.NotRun,
                "Pending-reboot state could not be read",
                new Dictionary<string, string>
                {
                    ["errorCode"] = signals.Error!.Code.ToString(),
                    ["errorMessage"] = signals.Error.Message,
                },
                ["Verify remote registry/WMI access on the target and rerun the diagnostics."],
                capturedAtUtc)];
        }

        if (signals.Value.Count == 0)
        {
            return [BuildResult(
                DiagnosticStatus.Pass,
                "No reboot is pending",
                new Dictionary<string, string>(),
                [],
                capturedAtUtc)];
        }

        return [BuildResult(
            DiagnosticStatus.Warning,
            "A reboot is pending",
            new Dictionary<string, string> { ["signals"] = string.Join("; ", signals.Value) },
            ["Reboot the machine so pending updates and component changes become effective."],
            capturedAtUtc)];
    }

    private Result<List<string>> CollectLocalSignals()
    {
        var signals = new List<string>();
        if (HasLocalSubKey(ComponentBasedServicingKey, "RebootPending"))
        {
            signals.Add("Component Based Servicing: RebootPending");
        }

        if (HasLocalSubKey(AutoUpdateKey, "RebootRequired"))
        {
            signals.Add("Windows Update: RebootRequired");
        }

        Result<object?> pendingRenames =
            _registryReader.ReadLocalMachineValue(SessionManagerKey, "PendingFileRenameOperations");
        if (pendingRenames.IsSuccess && pendingRenames.Value is string[] { Length: > 0 })
        {
            signals.Add("Session Manager: PendingFileRenameOperations");
        }

        return Result.Success(signals);
    }

    private async Task<Result<List<string>>> CollectRemoteSignalsAsync(
        DiagnosticContext context, CancellationToken cancellationToken)
    {
        var signals = new List<string>();

        Result<bool> rebootPending = await RemoteSubKeyExistsAsync(
            context, $@"{ComponentBasedServicingKey}\RebootPending", cancellationToken);
        if (rebootPending.IsFailure)
        {
            return Result.Failure<List<string>>(rebootPending.Error!);
        }

        if (rebootPending.Value)
        {
            signals.Add("Component Based Servicing: RebootPending");
        }

        Result<bool> rebootRequired = await RemoteSubKeyExistsAsync(
            context, $@"{AutoUpdateKey}\RebootRequired", cancellationToken);
        if (rebootRequired.IsFailure)
        {
            return Result.Failure<List<string>>(rebootRequired.Error!);
        }

        if (rebootRequired.Value)
        {
            signals.Add("Windows Update: RebootRequired");
        }

        Result<WmiInstance> pendingRenames = await _wmiQueryService.InvokeMethodAsync(
            context.Target, context.Credentials, context.Connection,
            RegistryNamespace, StdRegProvClass, "GetMultiStringValue",
            new Dictionary<string, object?>
            {
                ["hDefKey"] = HkeyLocalMachine,
                ["sSubKeyName"] = SessionManagerKey,
                ["sValueName"] = "PendingFileRenameOperations",
            },
            cancellationToken);
        if (pendingRenames.IsFailure)
        {
            return Result.Failure<List<string>>(pendingRenames.Error!);
        }

        if ((pendingRenames.Value.GetInteger("ReturnValue") ?? -1) == 0
            && pendingRenames.Value.GetRawValue("sValue") is string[] { Length: > 0 })
        {
            signals.Add("Session Manager: PendingFileRenameOperations");
        }

        return Result.Success(signals);
    }

    private async Task<Result<bool>> RemoteSubKeyExistsAsync(
        DiagnosticContext context, string keyPath, CancellationToken cancellationToken)
    {
        Result<WmiInstance> result = await _wmiQueryService.InvokeMethodAsync(
            context.Target, context.Credentials, context.Connection,
            RegistryNamespace, StdRegProvClass, "EnumKey",
            new Dictionary<string, object?>
            {
                ["hDefKey"] = HkeyLocalMachine,
                ["sSubKeyName"] = keyPath,
            },
            cancellationToken);
        return result.IsFailure
            ? Result.Failure<bool>(result.Error!)
            : Result.Success((result.Value.GetInteger("ReturnValue") ?? -1) == 0);
    }

    private bool HasLocalSubKey(string parentKeyPath, string subKeyName)
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
