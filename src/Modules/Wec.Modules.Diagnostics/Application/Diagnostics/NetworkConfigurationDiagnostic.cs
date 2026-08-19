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

        IReadOnlyList<NetworkAdapterInfo> relevantAdapters =
            NetworkAdapterSelection.RelevantAdapters(adaptersResult.Value);
        IReadOnlyList<NetworkAdapterInfo> secondaryAdapters =
            NetworkAdapterSelection.SecondaryAdapters(adaptersResult.Value);

        var results = new List<DiagnosticResult>();

        if (adaptersResult.Value.Count == 0)
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

        if (relevantAdapters.Count == 0)
        {
            results.Add(BuildResult(
                DiagnosticStatus.Warning,
                "No relevant IP-capable network adapter was identified",
                "Network adapters",
                new Dictionary<string, string>
                {
                    ["activeAdapterCount"] = adaptersResult.Value.Count.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    ["secondaryAdapters"] = DescribeSecondaryAdapters(secondaryAdapters),
                },
                [
                    "Check whether the expected Ethernet, Wi-Fi, or routed VPN adapter has a usable IPv4 address.",
                    "Review filter and virtual adapters only after confirming the primary network path.",
                ],
                capturedAtUtc));
            return Task.FromResult<IReadOnlyList<DiagnosticResult>>(results);
        }

        var evidence = new Dictionary<string, string>
        {
            ["relevantAdapters"] = relevantAdapters.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["secondaryAdapterCount"] = secondaryAdapters.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        foreach (NetworkAdapterInfo adapter in relevantAdapters)
        {
            evidence[$"adapter: {adapter.Name}"] = DescribeAdapter(adapter);
        }

        if (secondaryAdapters.Count > 0)
        {
            evidence["secondaryAdapters"] = DescribeSecondaryAdapters(secondaryAdapters);
        }

        bool anyGateway = NetworkAdapterSelection.SelectGateway(relevantAdapters) is not null;
        bool anyDnsServer = relevantAdapters.Any(adapter => adapter.DnsServers.Count > 0);

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

        return Task.FromResult<IReadOnlyList<DiagnosticResult>>(results);
    }

    internal static bool IsVirtualAdapter(NetworkAdapterInfo adapter) =>
        NetworkAdapterSelection.IsSecondaryAdapter(adapter);

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
        string route = adapter.IsPreferredRoute switch
        {
            true => "preferred route",
            false => "not preferred",
            null => "route unknown",
        };
        return $"{addresses} · GW: {JoinOrDash(adapter.GatewayAddresses)} · DNS: {JoinOrDash(adapter.DnsServers)}"
            + $" · MAC: {adapter.MacAddress ?? "—"} · {linkSpeed} · {dhcp} · {adapter.InterfaceType ?? "—"}"
            + $" · ifIndex {adapter.InterfaceIndex?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—"}"
            + $" · {route}";
    }

    private static string DescribeSecondaryAdapters(IReadOnlyList<NetworkAdapterInfo> adapters) =>
        adapters.Count == 0
            ? "(none)"
            : string.Join("; ", adapters.Select(adapter => $"{adapter.Name} ({adapter.Description})"));

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
