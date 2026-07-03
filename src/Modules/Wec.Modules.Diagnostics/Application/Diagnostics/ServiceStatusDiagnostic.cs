using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Diagnostics.Domain;

namespace Wec.Modules.Diagnostics.Application.Diagnostics;

internal sealed class ServiceStatusDiagnostic : IDiagnostic
{
    private const string CimV2Namespace = @"root\cimv2";

    private readonly IWmiQueryService _wmiQueryService;
    private readonly DiagnosticsOptions _options;
    private readonly IClock _clock;

    public ServiceStatusDiagnostic(
        IWmiQueryService wmiQueryService,
        IOptions<DiagnosticsOptions> options,
        IClock clock)
    {
        _wmiQueryService = wmiQueryService;
        _options = options.Value;
        _clock = clock;
    }

    public string DiagnosticId => "WEC-DIAG-SYS-SERVICES";

    public async Task<IReadOnlyList<DiagnosticResult>> EvaluateAsync(DiagnosticContext context, CancellationToken cancellationToken)
    {
        if (_options.MonitoredServices.Length == 0)
        {
            return [BuildResult(
                DiagnosticStatus.NotRun,
                "No services configured for the status check",
                new Dictionary<string, string> { ["configuredServices"] = "(empty)" },
                ["Configure Wec:Diagnostics:MonitoredServices (e.g. [\"Dhcp\", \"Dnscache\"])."])];
        }

        string condition = string.Join(" OR ", _options.MonitoredServices
            .Select(service => $"Name = '{EscapeWqlLiteral(service)}'"));
        Result<IReadOnlyList<WmiInstance>> services = await _wmiQueryService.QueryAsync(
            context,
            CimV2Namespace,
            $"SELECT Name, State, StartMode FROM Win32_Service WHERE {condition}",
            cancellationToken);

        if (services.IsFailure)
        {
            return [BuildResult(
                DiagnosticStatus.NotRun,
                "Service states could not be read",
                new Dictionary<string, string>
                {
                    ["errorCode"] = services.Error!.Code.ToString(),
                    ["errorMessage"] = services.Error.Message,
                },
                ["Verify the Windows Management Instrumentation service and rerun the diagnostics."])];
        }

        var statesByName = services.Value.ToDictionary(
            service => service.GetString("Name") ?? string.Empty,
            service => service,
            StringComparer.OrdinalIgnoreCase);

        var evidence = new Dictionary<string, string>();
        var problems = new List<string>();

        foreach (string serviceName in _options.MonitoredServices)
        {
            if (!statesByName.TryGetValue(serviceName, out WmiInstance? service))
            {
                evidence[serviceName] = "not installed";
                problems.Add($"Service '{serviceName}' is not installed on this machine.");
                continue;
            }

            string state = service.GetString("State") ?? "unknown";
            string startMode = service.GetString("StartMode") ?? "unknown";
            evidence[serviceName] = $"{state} ({startMode})";

            bool stoppedAutoService =
                !string.Equals(state, "Running", StringComparison.OrdinalIgnoreCase)
                && string.Equals(startMode, "Auto", StringComparison.OrdinalIgnoreCase);
            if (stoppedAutoService)
            {
                problems.Add($"Automatic service '{serviceName}' is not running (state: {state}).");
            }
        }

        if (problems.Count > 0)
        {
            return [BuildResult(
                DiagnosticStatus.Warning,
                "Monitored services show problems",
                evidence,
                [.. problems, "Check the service in services.msc and inspect the System event log for service errors."])];
        }

        return [BuildResult(
            DiagnosticStatus.Pass,
            "All monitored services look healthy",
            evidence,
            [])];
    }

    private static string EscapeWqlLiteral(string value) =>
        value.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("'", @"\'", StringComparison.Ordinal);

    private DiagnosticResult BuildResult(
        DiagnosticStatus status,
        string title,
        IReadOnlyDictionary<string, string> evidence,
        IReadOnlyList<string> nextSteps) => new(
        DiagnosticId,
        title,
        status,
        DiagnosticCategory.Services,
        "Windows services",
        evidence,
        nextSteps,
        RequiredPrivilege: null,
        _clock.UtcNow);
}
