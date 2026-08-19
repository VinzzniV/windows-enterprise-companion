using Wec.Core.Abstractions;
using Wec.Modules.Diagnostics.Application.Diagnostics;

namespace Wec.Modules.Diagnostics.Tests.Application;

public sealed class NetworkAdapterSelectionTests
{
    [Fact]
    public void CapturedMixedWindowsAdapters_KeepOnlyTheRoutedIpAdapterPrimary()
    {
        IReadOnlyList<NetworkAdapterInfo> adapters =
        [
            Adapter(
                "Ethernet", "Intel(R) Ethernet Connection I219-LM", "10.42.8.25",
                ["fe80::d1%12", "10.42.8.1"], preferred: true, interfaceIndex: 12),
            Adapter("Npcap Loopback Adapter", "Npcap Packet Driver (NPCAP)", null, []),
            Adapter("WFP Native MAC Layer LightWeight Filter", "WFP filter interface", null, []),
            Adapter("Ethernet QoS", "Microsoft QoS Packet Scheduler", null, []),
            Adapter("vEthernet (Default Switch)", "Hyper-V Virtual Ethernet Adapter", "172.28.224.1", []),
        ];

        NetworkAdapterInfo relevant = Assert.Single(NetworkAdapterSelection.RelevantAdapters(adapters));
        Assert.Equal("Ethernet", relevant.Name);
        Assert.Equal(4, NetworkAdapterSelection.SecondaryAdapters(adapters).Count);

        GatewaySelection selection = Assert.IsType<GatewaySelection>(
            NetworkAdapterSelection.SelectGateway(adapters));
        Assert.Equal("10.42.8.1", selection.Gateway);
        Assert.Equal("Ethernet", selection.Adapter.Name);
        Assert.Equal("Windows preferred IPv4 route", selection.Reason);
    }

    [Fact]
    public void ApipaAndFilterOnlyAdapters_AreNotPresentedAsHealthyPrimaryNetworking()
    {
        IReadOnlyList<NetworkAdapterInfo> adapters =
        [
            Adapter("Ethernet", "Intel Ethernet", "169.254.10.20", []),
            Adapter("Npcap Loopback Adapter", "Npcap Packet Driver", null, []),
        ];

        Assert.Empty(NetworkAdapterSelection.RelevantAdapters(adapters));
        Assert.Equal(2, NetworkAdapterSelection.SecondaryAdapters(adapters).Count);
    }

    [Fact]
    public void RoutedVpnAdapter_IsRelevantWhenWindowsSelectedItsRoute()
    {
        NetworkAdapterInfo vpn = Adapter(
            "WireGuard", "WireGuard Tunnel", "10.8.0.2", ["10.8.0.1"],
            preferred: true, interfaceIndex: 42);

        Assert.Equal(vpn, Assert.Single(NetworkAdapterSelection.RelevantAdapters([vpn])));
        Assert.Equal("10.8.0.1", NetworkAdapterSelection.SelectGateway([vpn])!.Gateway);
    }

    private static NetworkAdapterInfo Adapter(
        string name,
        string description,
        string? ipv4,
        IReadOnlyList<string> gateways,
        bool? preferred = null,
        int? interfaceIndex = null) => new(
        name,
        description,
        ipv4 is null ? [] : [new Ipv4AddressInfo(ipv4, 24)],
        gateways,
        ["10.42.8.10"],
        InterfaceIndex: interfaceIndex,
        IsPreferredRoute: preferred);
}
