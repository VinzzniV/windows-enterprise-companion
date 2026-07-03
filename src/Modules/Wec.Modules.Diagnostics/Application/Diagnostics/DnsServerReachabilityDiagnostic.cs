using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Diagnostics.Domain;

namespace Wec.Modules.Diagnostics.Application.Diagnostics;

internal sealed class DnsServerReachabilityDiagnostic : IDiagnostic
{
    private readonly INetworkInfoProvider _networkInfoProvider;
    private readonly IPingProbe _pingProbe;
    private readonly DiagnosticsOptions _options;
    private readonly IClock _clock;

    public DnsServerReachabilityDiagnostic(
        INetworkInfoProvider networkInfoProvider,
        IPingProbe pingProbe,
        Microsoft.Extensions.Options.IOptions<DiagnosticsOptions> options,
        IClock clock)
    {
        _networkInfoProvider = networkInfoProvider;
        _pingProbe = pingProbe;
        _options = options.Value;
        _clock = clock;
    }

    public string DiagnosticId => "WEC-DIAG-DNS-SERVERS";

    public async Task<IReadOnlyList<DiagnosticResult>> EvaluateAsync(DiagnosticContext context, CancellationToken cancellationToken)
    {
        if (!context.Target.IsLocal)
        {
            return [DiagnosticResults.LocalPerspective(
                DiagnosticId, "DNS server reachability", DiagnosticCategory.Dns,
                "DNS servers", context.Target.DisplayName, _clock.UtcNow)];
        }

        Result<IReadOnlyList<NetworkAdapterInfo>> adapters = _networkInfoProvider.GetActiveAdapters();
        DateTimeOffset capturedAtUtc = _clock.UtcNow;

        if (adapters.IsFailure)
        {
            return [BuildResult(
                DiagnosticStatus.NotRun,
                "Configured DNS servers could not be read",
                new Dictionary<string, string> { ["errorMessage"] = adapters.Error!.Message },
                ["Verify that network services are running and rerun the diagnostics."],
                capturedAtUtc)];
        }

        List<string> dnsServers = adapters.Value
            .SelectMany(adapter => adapter.DnsServers)
            .Distinct()
            .ToList();

        if (dnsServers.Count == 0)
        {
            return [BuildResult(
                DiagnosticStatus.Warning,
                "No DNS servers are configured",
                new Dictionary<string, string> { ["dnsServers"] = "(none)" },
                ["Without DNS servers name resolution fails — check DHCP or the static configuration."],
                capturedAtUtc)];
        }

        var evidence = new Dictionary<string, string>();
        var reachableCount = 0;
        foreach (string dnsServer in dnsServers)
        {
            Result<PingProbeReply> ping = await _pingProbe.SendAsync(dnsServer, _options.ProbeTimeout, cancellationToken);
            if (ping.IsSuccess && ping.Value.Success)
            {
                reachableCount++;
                evidence[dnsServer] = $"reachable ({ping.Value.RoundtripMilliseconds} ms)";
            }
            else
            {
                evidence[dnsServer] = ping.IsSuccess ? ping.Value.Status : ping.Error!.Message;
            }
        }

        capturedAtUtc = _clock.UtcNow;

        if (reachableCount == 0)
        {
            return [BuildResult(
                DiagnosticStatus.Warning,
                "No configured DNS server answered a ping",
                evidence,
                [
                    "ICMP may be blocked — cross-check with: nslookup against each server.",
                    "If the servers are really down, name resolution runs on cache only.",
                ],
                capturedAtUtc)];
        }

        return [BuildResult(
            DiagnosticStatus.Pass,
            $"{reachableCount} of {dnsServers.Count} DNS servers reachable",
            evidence,
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
        DiagnosticCategory.Dns,
        "Configured DNS servers",
        evidence,
        nextSteps,
        RequiredPrivilege: null,
        capturedAtUtc);
}
