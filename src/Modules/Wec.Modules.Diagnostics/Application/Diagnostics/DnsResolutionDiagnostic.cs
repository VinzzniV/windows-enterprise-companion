using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Diagnostics.Domain;

namespace Wec.Modules.Diagnostics.Application.Diagnostics;

internal sealed class DnsResolutionDiagnostic : IDiagnostic
{
    private readonly IDnsResolver _dnsResolver;
    private readonly DiagnosticsOptions _options;
    private readonly IClock _clock;

    public DnsResolutionDiagnostic(IDnsResolver dnsResolver, IOptions<DiagnosticsOptions> options, IClock clock)
    {
        _dnsResolver = dnsResolver;
        _options = options.Value;
        _clock = clock;
    }

    public string DiagnosticId => "WEC-DIAG-NET-DNS";

    public async Task<IReadOnlyList<DiagnosticResult>> EvaluateAsync(CancellationToken cancellationToken)
    {
        string probeHostname = _options.DnsProbeHostname;
        Result<IReadOnlyList<string>> resolution = await _dnsResolver.ResolveAsync(probeHostname, cancellationToken);
        DateTimeOffset capturedAtUtc = _clock.UtcNow;

        if (resolution.IsFailure)
        {
            return [BuildResult(
                DiagnosticStatus.Fail,
                $"DNS resolution of {probeHostname} failed",
                new Dictionary<string, string>
                {
                    ["probeHostname"] = probeHostname,
                    ["errorCode"] = resolution.Error!.Code.ToString(),
                    ["errorMessage"] = resolution.Error.Message,
                    ["errorDetails"] = resolution.Error.Details ?? "—",
                },
                [
                    "Check whether the configured DNS servers are reachable (see network configuration).",
                    "Check gateway reachability first — without a route, DNS cannot work either.",
                    "In proxy-only or offline environments external DNS may be blocked by design.",
                    $"Cross-check manually: nslookup {probeHostname}",
                ],
                capturedAtUtc)];
        }

        if (resolution.Value.Count == 0)
        {
            return [BuildResult(
                DiagnosticStatus.Warning,
                $"DNS resolution of {probeHostname} returned no addresses",
                new Dictionary<string, string> { ["probeHostname"] = probeHostname, ["addresses"] = "(none)" },
                [$"Cross-check manually: nslookup {probeHostname}"],
                capturedAtUtc)];
        }

        return [BuildResult(
            DiagnosticStatus.Pass,
            "DNS resolution works",
            new Dictionary<string, string>
            {
                ["probeHostname"] = probeHostname,
                ["addresses"] = string.Join(", ", resolution.Value),
            },
            [],
            capturedAtUtc)];
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
        DiagnosticCategory.Network,
        $"DNS resolution ({_options.DnsProbeHostname})",
        evidence,
        nextSteps,
        RequiredPrivilege: null,
        capturedAtUtc);
}
