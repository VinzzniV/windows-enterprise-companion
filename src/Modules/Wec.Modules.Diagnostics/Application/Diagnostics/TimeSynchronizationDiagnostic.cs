using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Diagnostics.Domain;

namespace Wec.Modules.Diagnostics.Application.Diagnostics;

internal sealed class TimeSynchronizationDiagnostic : IDiagnostic
{
    private const string CimV2Namespace = @"root\cimv2";
    private const string W32TimeParametersKey = @"SYSTEM\CurrentControlSet\Services\W32Time\Parameters";

    private readonly IRegistryReader _registryReader;
    private readonly IWmiQueryService _wmiQueryService;
    private readonly IClock _clock;

    public TimeSynchronizationDiagnostic(
        IRegistryReader registryReader,
        IWmiQueryService wmiQueryService,
        IClock clock)
    {
        _registryReader = registryReader;
        _wmiQueryService = wmiQueryService;
        _clock = clock;
    }

    public string DiagnosticId => "WEC-DIAG-SYS-TIMESYNC";

    public async Task<IReadOnlyList<DiagnosticResult>> EvaluateAsync(DiagnosticContext context, CancellationToken cancellationToken)
    {
        Result<object?> syncType = await _registryReader.ReadLocalMachineValueAsync(
            context.Target, context.Credentials, context.Connection, W32TimeParametersKey, "Type", cancellationToken);
        DateTimeOffset capturedAtUtc = _clock.UtcNow;

        if (syncType.IsFailure)
        {
            return [BuildProviderFailure(
                "Time synchronization type could not be read",
                syncType.Error!,
                new Dictionary<string, string>(),
                "Verify registry read permissions and rerun the diagnostics.",
                capturedAtUtc)];
        }

        if (syncType.Value is not null and not string)
        {
            return [BuildIncompleteResult(
                "Time synchronization type has an unsupported registry value",
                new Dictionary<string, string>
                {
                    ["valueType"] = syncType.Value.GetType().Name,
                },
                "Verify the W32Time Type registry value and rerun the diagnostics.",
                capturedAtUtc)];
        }

        string? configuredTypeValue = syncType.Value as string;
        string configuredType = string.IsNullOrWhiteSpace(configuredTypeValue)
            ? "(missing)"
            : configuredTypeValue;
        Result<object?> ntpServerValue = await _registryReader.ReadLocalMachineValueAsync(
            context.Target, context.Credentials, context.Connection, W32TimeParametersKey, "NtpServer", cancellationToken);
        capturedAtUtc = _clock.UtcNow;

        if (ntpServerValue.IsFailure)
        {
            return [BuildProviderFailure(
                "Time synchronization NTP server could not be read",
                ntpServerValue.Error!,
                new Dictionary<string, string> { ["syncType"] = configuredType },
                "Verify registry read permissions and rerun the diagnostics.",
                capturedAtUtc)];
        }

        if (ntpServerValue.Value is not null and not string)
        {
            return [BuildIncompleteResult(
                "Time synchronization NTP server has an unsupported registry value",
                new Dictionary<string, string>
                {
                    ["syncType"] = configuredType,
                    ["valueType"] = ntpServerValue.Value.GetType().Name,
                },
                "Verify the W32Time NtpServer registry value and rerun the diagnostics.",
                capturedAtUtc)];
        }

        string? ntpServerValueText = ntpServerValue.Value as string;
        string ntpServer = string.IsNullOrWhiteSpace(ntpServerValueText)
            ? "(not set)"
            : ntpServerValueText;

        Result<IReadOnlyList<WmiInstance>> service = await _wmiQueryService.QueryAsync(
            context,
            CimV2Namespace,
            "SELECT Name, State, StartMode FROM Win32_Service WHERE Name = 'W32Time'",
            cancellationToken);
        capturedAtUtc = _clock.UtcNow;

        var configurationEvidence = new Dictionary<string, string>
        {
            ["syncType"] = configuredType,
            ["ntpServer"] = ntpServer,
        };

        if (service.IsFailure)
        {
            return [BuildProviderFailure(
                "Windows Time service state could not be read",
                service.Error!,
                configurationEvidence,
                "Verify Windows Management Instrumentation access and rerun the diagnostics.",
                capturedAtUtc)];
        }

        if (service.Value.Count == 0)
        {
            configurationEvidence["w32TimeService"] = "not installed";
            return [BuildResult(
                DiagnosticStatus.Warning,
                "Windows Time service is not installed",
                configurationEvidence,
                ["Restore the Windows Time service and rerun the diagnostics."],
                requiredPrivilege: null,
                capturedAtUtc)];
        }

        string? serviceState = service.Value[0].GetString("State");
        string? serviceStartMode = service.Value[0].GetString("StartMode");
        if (string.IsNullOrWhiteSpace(serviceState) || string.IsNullOrWhiteSpace(serviceStartMode))
        {
            configurationEvidence["w32TimeService"] =
                $"{serviceState ?? "(missing)"} ({serviceStartMode ?? "(missing)"})";
            return [BuildIncompleteResult(
                "Windows Time service returned incomplete state information",
                configurationEvidence,
                "Verify Windows Management Instrumentation and rerun the diagnostics.",
                capturedAtUtc)];
        }

        configurationEvidence["w32TimeService"] = $"{serviceState} ({serviceStartMode})";
        IReadOnlyDictionary<string, string> evidence = configurationEvidence;

        if (string.IsNullOrWhiteSpace(configuredTypeValue))
        {
            return [BuildResult(
                DiagnosticStatus.Warning,
                "Time synchronization type is not configured",
                evidence,
                ["Configure the W32Time synchronization type and rerun the diagnostics."],
                requiredPrivilege: null,
                capturedAtUtc)];
        }

        if (string.Equals(configuredType, "NoSync", StringComparison.OrdinalIgnoreCase))
        {
            return [BuildResult(
                DiagnosticStatus.Warning,
                "Time synchronization is disabled (NoSync)",
                evidence,
                [
                    "Enable time synchronization — clock drift breaks Kerberos authentication and TLS validation.",
                    "Cross-check with: w32tm /query /status",
                    "On domain-joined machines the type should normally be NT5DS (domain hierarchy).",
                ],
                requiredPrivilege: null,
                capturedAtUtc)];
        }

        bool knownSyncType = string.Equals(configuredType, "NTP", StringComparison.OrdinalIgnoreCase)
            || string.Equals(configuredType, "NT5DS", StringComparison.OrdinalIgnoreCase)
            || string.Equals(configuredType, "AllSync", StringComparison.OrdinalIgnoreCase);
        if (!knownSyncType)
        {
            return [BuildResult(
                DiagnosticStatus.Warning,
                $"Time synchronization type is not recognized ({configuredType})",
                evidence,
                ["Verify the W32Time Type registry value and rerun the diagnostics."],
                requiredPrivilege: null,
                capturedAtUtc)];
        }

        if (string.Equals(serviceStartMode, "Disabled", StringComparison.OrdinalIgnoreCase))
        {
            return [BuildResult(
                DiagnosticStatus.Warning,
                "Windows Time service is disabled",
                evidence,
                [
                    "Set the Windows Time (W32Time) service back to Manual (Trigger Start) or Automatic.",
                    "Cross-check with: w32tm /query /status",
                ],
                requiredPrivilege: null,
                capturedAtUtc)];
        }

        bool recognizedStartMode = string.Equals(serviceStartMode, "Manual", StringComparison.OrdinalIgnoreCase)
            || string.Equals(serviceStartMode, "Auto", StringComparison.OrdinalIgnoreCase)
            || string.Equals(serviceStartMode, "Automatic", StringComparison.OrdinalIgnoreCase);
        if (!recognizedStartMode)
        {
            return [BuildResult(
                DiagnosticStatus.Warning,
                $"Windows Time service start mode is not recognized ({serviceStartMode})",
                evidence,
                ["Verify the Windows Time service configuration and rerun the diagnostics."],
                requiredPrivilege: null,
                capturedAtUtc)];
        }

        bool stoppedManualService = string.Equals(serviceState, "Stopped", StringComparison.OrdinalIgnoreCase)
            && string.Equals(serviceStartMode, "Manual", StringComparison.OrdinalIgnoreCase);
        if (!string.Equals(serviceState, "Running", StringComparison.OrdinalIgnoreCase) && !stoppedManualService)
        {
            return [BuildResult(
                DiagnosticStatus.Warning,
                $"Windows Time service is not running ({serviceState})",
                evidence,
                [
                    "Start the Windows Time service and inspect its service configuration.",
                    "Cross-check with: w32tm /query /status",
                ],
                requiredPrivilege: null,
                capturedAtUtc)];
        }

        bool requiresNtpServer = string.Equals(configuredType, "NTP", StringComparison.OrdinalIgnoreCase)
            || string.Equals(configuredType, "AllSync", StringComparison.OrdinalIgnoreCase);
        if (requiresNtpServer && string.IsNullOrWhiteSpace(ntpServerValueText))
        {
            return [BuildResult(
                DiagnosticStatus.Warning,
                $"Time synchronization has no NTP server configured ({configuredType})",
                evidence,
                ["Configure a trusted NTP source and rerun the diagnostics."],
                requiredPrivilege: null,
                capturedAtUtc)];
        }

        // A stopped W32Time service with Manual (trigger) start is normal on
        // workgroup machines — the service state is evidence, not a warning
        return [BuildResult(
            DiagnosticStatus.Pass,
            $"Time synchronization is configured ({configuredType})",
            evidence,
            [],
            requiredPrivilege: null,
            capturedAtUtc)];
    }

    private DiagnosticResult BuildProviderFailure(
        string title,
        Error error,
        IReadOnlyDictionary<string, string> availableEvidence,
        string nextStep,
        DateTimeOffset capturedAtUtc)
    {
        var evidence = new Dictionary<string, string>(availableEvidence)
        {
            ["errorCode"] = error.Code.ToString(),
            ["errorMessage"] = error.Message,
        };
        return BuildResult(
            DiagnosticStatus.NotRun,
            title,
            evidence,
            [nextStep],
            error.RequiredPrivilege,
            capturedAtUtc);
    }

    private DiagnosticResult BuildIncompleteResult(
        string title,
        IReadOnlyDictionary<string, string> evidence,
        string nextStep,
        DateTimeOffset capturedAtUtc) => BuildResult(
        DiagnosticStatus.NotRun,
        title,
        evidence,
        [nextStep],
        requiredPrivilege: null,
        capturedAtUtc);

    private DiagnosticResult BuildResult(
        DiagnosticStatus status,
        string title,
        IReadOnlyDictionary<string, string> evidence,
        IReadOnlyList<string> nextSteps,
        Wec.Core.Privileges.PrivilegeLevel? requiredPrivilege,
        DateTimeOffset capturedAtUtc) => new(
        DiagnosticId,
        title,
        status,
        DiagnosticCategory.TimeSynchronization,
        "Windows Time service",
        evidence,
        nextSteps,
        requiredPrivilege,
        capturedAtUtc);
}
