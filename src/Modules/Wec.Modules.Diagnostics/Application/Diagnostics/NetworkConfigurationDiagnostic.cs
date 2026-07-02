using Wec.Core.Abstractions;
using Wec.Modules.Diagnostics.Domain;

namespace Wec.Modules.Diagnostics.Application.Diagnostics;

internal sealed class NetworkConfigurationDiagnostic : IDiagnostic
{
    private readonly INetworkInfoProvider _networkInfoProvider;
    private readonly IClock _clock;

    public NetworkConfigurationDiagnostic(INetworkInfoProvider networkInfoProvider, IClock clock)
    {
        _networkInfoProvider = networkInfoProvider;
        _clock = clock;
    }

    public string DiagnosticId => "WEC-DIAG-NET-CONFIG";

    public Task<IReadOnlyList<DiagnosticResult>> EvaluateAsync(CancellationToken cancellationToken)
    {
        var adaptersResult = _networkInfoProvider.GetActiveAdapters();
        DateTimeOffset capturedAtUtc = _clock.UtcNow;

        if (adaptersResult.IsFailure)
        {
            return Task.FromResult<IReadOnlyList<DiagnosticResult>>([BuildResult(
                DiagnosticStatus.NotRun,
                "Network configuration could not be read",
                new Dictionary<string, string>
                {
                    ["errorCode"] = adaptersResult.Error!.Code.ToString(),
                    ["errorMessage"] = adaptersResult.Error.Message,
                },
                ["Verify that network services are running and rerun the diagnostics."],
                capturedAtUtc)]);
        }

        IReadOnlyList<NetworkAdapterInfo> adapters = adaptersResult.Value;

        if (adapters.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<DiagnosticResult>>([BuildResult(
                DiagnosticStatus.Fail,
                "No active network adapter",
                new Dictionary<string, string> { ["activeAdapters"] = "0" },
                [
                    "Check the physical connection (cable, Wi-Fi radio switch).",
                    "Verify the adapter is enabled in Windows network settings.",
                    "Check the adapter driver in Device Manager.",
                ],
                capturedAtUtc)]);
        }

        var evidence = new Dictionary<string, string>
        {
            ["activeAdapters"] = adapters.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        foreach (NetworkAdapterInfo adapter in adapters)
        {
            string addresses = adapter.Ipv4Addresses.Count > 0
                ? string.Join(", ", adapter.Ipv4Addresses.Select(ip => $"{ip.Address}/{ip.PrefixLength}"))
                : "(no IPv4 address)";
            evidence[$"adapter: {adapter.Name}"] =
                $"{addresses} · GW: {JoinOrDash(adapter.GatewayAddresses)} · DNS: {JoinOrDash(adapter.DnsServers)}";
        }

        bool anyGateway = adapters.Any(adapter => adapter.GatewayAddresses.Count > 0);
        bool anyDnsServer = adapters.Any(adapter => adapter.DnsServers.Count > 0);

        if (!anyGateway || !anyDnsServer)
        {
            var nextSteps = new List<string>();
            if (!anyGateway)
            {
                nextSteps.Add("No default gateway is configured — check DHCP or the static IP configuration.");
            }

            if (!anyDnsServer)
            {
                nextSteps.Add("No DNS server is configured — name resolution will fail.");
            }

            nextSteps.Add("Compare with a working device in the same network.");

            return Task.FromResult<IReadOnlyList<DiagnosticResult>>([BuildResult(
                DiagnosticStatus.Warning,
                anyGateway ? "Network configured without DNS servers" : "Network configured without default gateway",
                evidence,
                nextSteps,
                capturedAtUtc)]);
        }

        return Task.FromResult<IReadOnlyList<DiagnosticResult>>([BuildResult(
            DiagnosticStatus.Pass,
            "Network configuration looks complete",
            evidence,
            [],
            capturedAtUtc)]);
    }

    private static string JoinOrDash(IReadOnlyList<string> values) =>
        values.Count > 0 ? string.Join(", ", values) : "—";

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
        "Network adapters",
        evidence,
        nextSteps,
        RequiredPrivilege: null,
        capturedAtUtc);
}
