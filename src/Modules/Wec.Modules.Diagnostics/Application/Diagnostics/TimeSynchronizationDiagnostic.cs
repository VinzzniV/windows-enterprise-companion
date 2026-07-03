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
        if (!context.Target.IsLocal)
        {
            return [DiagnosticResults.LocalPerspective(
                DiagnosticId, "Time synchronization", DiagnosticCategory.TimeSynchronization,
                "Windows Time service", context.Target.DisplayName, _clock.UtcNow)];
        }

        Result<object?> syncType = _registryReader.ReadLocalMachineValue(W32TimeParametersKey, "Type");
        DateTimeOffset capturedAtUtc = _clock.UtcNow;

        if (syncType.IsFailure)
        {
            return [BuildResult(
                DiagnosticStatus.NotRun,
                "Time synchronization configuration could not be read",
                new Dictionary<string, string>
                {
                    ["errorCode"] = syncType.Error!.Code.ToString(),
                    ["errorMessage"] = syncType.Error.Message,
                },
                ["Verify registry read permissions and rerun the diagnostics."],
                syncType.Error.RequiredPrivilege,
                capturedAtUtc)];
        }

        string configuredType = syncType.Value as string ?? "(missing)";
        string ntpServer = _registryReader
            .ReadLocalMachineValue(W32TimeParametersKey, "NtpServer").Value as string ?? "(not set)";

        Result<IReadOnlyList<WmiInstance>> service = await _wmiQueryService.QueryAsync(
            CimV2Namespace,
            "SELECT Name, State, StartMode FROM Win32_Service WHERE Name = 'W32Time'",
            cancellationToken);
        capturedAtUtc = _clock.UtcNow;

        string serviceState = service.IsSuccess && service.Value.Count > 0
            ? service.Value[0].GetString("State") ?? "unknown"
            : "unknown";
        string serviceStartMode = service.IsSuccess && service.Value.Count > 0
            ? service.Value[0].GetString("StartMode") ?? "unknown"
            : "unknown";

        var evidence = new Dictionary<string, string>
        {
            ["syncType"] = configuredType,
            ["ntpServer"] = ntpServer,
            ["w32TimeService"] = $"{serviceState} ({serviceStartMode})",
        };

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
