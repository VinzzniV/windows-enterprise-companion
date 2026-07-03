using Wec.Core.Abstractions;
using Wec.Modules.Diagnostics.Domain;

namespace Wec.Modules.Diagnostics.Application.Diagnostics;

internal sealed class NetworkConfigurationDiagnostic : IDiagnostic
{
    // Virtualization/VPN/filter adapters that would otherwise drown out the
    // physical adapters in the result list
    private static readonly string[] VirtualAdapterMarkers =
    [
        "virtual", "vethernet", "hyper-v", "vmware", "virtualbox", "tap-", "tap ",
        "wintun", "wireguard", "openvpn", "loopback", "npcap", "bluetooth",
    ];

    private readonly INetworkInfoProvider _networkInfoProvider;
    private readonly IClock _clock;

    public NetworkConfigurationDiagnostic(INetworkInfoProvider networkInfoProvider, IClock clock)
    {
        _networkInfoProvider = networkInfoProvider;
        _clock = clock;
    }

    public string DiagnosticId => "WEC-DIAG-NET-CONFIG";

    public Task<IReadOnlyList<DiagnosticResult>> EvaluateAsync(DiagnosticContext context, CancellationToken cancellationToken)
    {
        if (!context.Target.IsLocal)
        {
            return Task.FromResult<IReadOnlyList<DiagnosticResult>>([DiagnosticResults.LocalPerspective(
                DiagnosticId, "Network configuration", DiagnosticCategory.Network,
                "Network adapters", context.Target.DisplayName, _clock.UtcNow)]);
        }

        var adaptersResult = _networkInfoProvider.GetActiveAdapters();
        DateTimeOffset capturedAtUtc = _clock.UtcNow;

        if (adaptersResult.IsFailure)
        {
            return Task.FromResult<IReadOnlyList<DiagnosticResult>>([BuildResult(
                DiagnosticStatus.NotRun,
                "Network configuration could not be read",
                "Network adapters",
                new Dictionary<string, string>
                {
                    ["errorCode"] = adaptersResult.Error!.Code.ToString(),
                    ["errorMessage"] = adaptersResult.Error.Message,
                },
                ["Verify that network services are running and rerun the diagnostics."],
                capturedAtUtc)]);
        }

        List<NetworkAdapterInfo> physicalAdapters = adaptersResult.Value
            .Where(adapter => !IsVirtualAdapter(adapter))
            .ToList();
        List<NetworkAdapterInfo> virtualAdapters = adaptersResult.Value
            .Where(IsVirtualAdapter)
            .ToList();

        var results = new List<DiagnosticResult>();

        if (physicalAdapters.Count == 0 && virtualAdapters.Count == 0)
        {
            results.Add(BuildResult(
                DiagnosticStatus.Fail,
                "No active network adapter",
                "Network adapters",
                new Dictionary<string, string> { ["activeAdapters"] = "0" },
                [
                    "Check the physical connection (cable, Wi-Fi radio switch).",
                    "Verify the adapter is enabled in Windows network settings.",
                    "Check the adapter driver in Device Manager.",
                ],
                capturedAtUtc));
            return Task.FromResult<IReadOnlyList<DiagnosticResult>>(results);
        }

        // A machine that only has virtual adapters up still needs a visible
        // primary result — fall back to evaluating those
        List<NetworkAdapterInfo> primaryAdapters = physicalAdapters.Count > 0 ? physicalAdapters : virtualAdapters;

        var evidence = new Dictionary<string, string>
        {
            ["activeAdapters"] = primaryAdapters.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        foreach (NetworkAdapterInfo adapter in primaryAdapters)
        {
            evidence[$"adapter: {adapter.Name}"] = DescribeAdapter(adapter);
        }

        bool anyGateway = primaryAdapters.Any(adapter => adapter.GatewayAddresses.Count > 0);
        bool anyDnsServer = primaryAdapters.Any(adapter => adapter.DnsServers.Count > 0);

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

            results.Add(BuildResult(
                DiagnosticStatus.Warning,
                anyGateway ? "Network configured without DNS servers" : "Network configured without default gateway",
                "Network adapters",
                evidence,
                nextSteps,
                capturedAtUtc));
        }
        else
        {
            results.Add(BuildResult(
                DiagnosticStatus.Pass,
                "Network configuration looks complete",
                "Network adapters",
                evidence,
                [],
                capturedAtUtc));
        }

        if (physicalAdapters.Count > 0 && virtualAdapters.Count > 0)
        {
            var virtualEvidence = new Dictionary<string, string>();
            foreach (NetworkAdapterInfo adapter in virtualAdapters)
            {
                virtualEvidence[$"adapter: {adapter.Name}"] = DescribeAdapter(adapter);
            }

            results.Add(BuildResult(
                DiagnosticStatus.Pass,
                $"{virtualAdapters.Count} virtual/filter adapters are active",
                "Virtual network adapters",
                virtualEvidence,
                [],
                capturedAtUtc));
        }

        return Task.FromResult<IReadOnlyList<DiagnosticResult>>(results);
    }

    internal static bool IsVirtualAdapter(NetworkAdapterInfo adapter)
    {
        string haystack = $"{adapter.Name} {adapter.Description}".ToLowerInvariant();
        return VirtualAdapterMarkers.Any(marker => haystack.Contains(marker, StringComparison.Ordinal));
    }

    private static string DescribeAdapter(NetworkAdapterInfo adapter)
    {
        string addresses = adapter.Ipv4Addresses.Count > 0
            ? string.Join(", ", adapter.Ipv4Addresses.Select(ip => $"{ip.Address}/{ip.PrefixLength}"))
            : "(no IPv4 address)";
        string linkSpeed = adapter.SpeedBitsPerSecond is > 0
            ? $"{adapter.SpeedBitsPerSecond / 1_000_000} Mbit/s"
            : "—";
        string dhcp = adapter.IsDhcpEnabled switch
        {
            true => "DHCP",
            false => "static",
            null => "—",
        };
        return $"{addresses} · GW: {JoinOrDash(adapter.GatewayAddresses)} · DNS: {JoinOrDash(adapter.DnsServers)}"
            + $" · MAC: {adapter.MacAddress ?? "—"} · {linkSpeed} · {dhcp} · {adapter.InterfaceType ?? "—"}";
    }

    private static string JoinOrDash(IReadOnlyList<string> values) =>
        values.Count > 0 ? string.Join(", ", values) : "—";

    private DiagnosticResult BuildResult(
        DiagnosticStatus status,
        string title,
        string affectedResource,
        IReadOnlyDictionary<string, string> evidence,
        IReadOnlyList<string> nextSteps,
        DateTimeOffset capturedAtUtc) => new(
        DiagnosticId,
        title,
        status,
        DiagnosticCategory.Network,
        affectedResource,
        evidence,
        nextSteps,
        RequiredPrivilege: null,
        capturedAtUtc);
}
