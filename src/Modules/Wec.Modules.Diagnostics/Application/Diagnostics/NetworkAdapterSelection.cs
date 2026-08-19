using System.Net;
using System.Net.Sockets;
using Wec.Core.Abstractions;

namespace Wec.Modules.Diagnostics.Application.Diagnostics;

internal sealed record GatewaySelection(
    NetworkAdapterInfo Adapter,
    string Gateway,
    string Reason);

internal static class NetworkAdapterSelection
{
    private static readonly string[] SecondaryAdapterMarkers =
    [
        "virtual", "vethernet", "hyper-v", "vmware", "virtualbox", "tap-", "tap ",
        "wintun", "wireguard", "openvpn", "loopback", "npcap", "bluetooth", "filter",
        "lightweight", "packet", "qos", "wfp", "miniport", "teredo", "isatap", "6to4",
    ];

    public static IReadOnlyList<NetworkAdapterInfo> RelevantAdapters(
        IReadOnlyList<NetworkAdapterInfo> adapters) => adapters
        .Where(IsRelevantAdapter)
        .OrderByDescending(adapter => adapter.IsPreferredRoute == true)
        .ThenBy(adapter => adapter.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public static IReadOnlyList<NetworkAdapterInfo> SecondaryAdapters(
        IReadOnlyList<NetworkAdapterInfo> adapters)
    {
        IReadOnlyList<NetworkAdapterInfo> relevant = RelevantAdapters(adapters);
        return adapters
            .Where(adapter => !relevant.Contains(adapter))
            .OrderBy(adapter => adapter.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static GatewaySelection? SelectGateway(IReadOnlyList<NetworkAdapterInfo> adapters)
    {
        var candidates = adapters
            .SelectMany(adapter => adapter.GatewayAddresses.Select(gateway => new
            {
                Adapter = adapter,
                Gateway = gateway,
                Parsed = IPAddress.TryParse(gateway, out IPAddress? parsed) ? parsed : null,
            }))
            .Where(candidate => candidate.Parsed is not null && IsUsableGateway(candidate.Parsed))
            .OrderByDescending(candidate => candidate.Adapter.IsPreferredRoute == true)
            .ThenByDescending(candidate => IsRelevantAdapter(candidate.Adapter))
            .ThenByDescending(candidate => candidate.Parsed!.AddressFamily == AddressFamily.InterNetwork)
            .ThenBy(candidate => candidate.Parsed!.IsIPv6LinkLocal)
            .ThenBy(candidate => candidate.Adapter.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Gateway, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (candidates is null)
        {
            return null;
        }

        string reason = candidates.Adapter.IsPreferredRoute == true
            ? candidates.Parsed!.AddressFamily == AddressFamily.InterNetwork
                ? "Windows preferred IPv4 route"
                : "Windows preferred route"
            : IsRelevantAdapter(candidates.Adapter)
                ? candidates.Parsed!.AddressFamily == AddressFamily.InterNetwork
                    ? "IPv4 gateway on a relevant IP-capable adapter"
                    : "gateway on a relevant IP-capable adapter"
                : "fallback gateway; Windows preferred route was unavailable";

        return new GatewaySelection(candidates.Adapter, candidates.Gateway, reason);
    }

    internal static bool IsSecondaryAdapter(NetworkAdapterInfo adapter)
    {
        string haystack = $"{adapter.Name} {adapter.Description}".ToLowerInvariant();
        return SecondaryAdapterMarkers.Any(marker => haystack.Contains(marker, StringComparison.Ordinal));
    }

    internal static bool HasUsableIpv4Address(NetworkAdapterInfo adapter) =>
        adapter.Ipv4Addresses.Any(address =>
            IPAddress.TryParse(address.Address, out IPAddress? parsed)
            && parsed.AddressFamily == AddressFamily.InterNetwork
            && !IPAddress.IsLoopback(parsed)
            && !parsed.Equals(IPAddress.Any)
            && !parsed.Equals(IPAddress.Broadcast)
            && !IsAutomaticPrivateAddress(parsed));

    private static bool IsRelevantAdapter(NetworkAdapterInfo adapter) =>
        (HasUsableIpv4Address(adapter) || adapter.GatewayAddresses.Any(IsUsableGateway))
        && (!IsSecondaryAdapter(adapter) || adapter.IsPreferredRoute == true);

    private static bool IsUsableGateway(string address) =>
        IPAddress.TryParse(address, out IPAddress? parsed) && IsUsableGateway(parsed);

    private static bool IsAutomaticPrivateAddress(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        return bytes[0] == 169 && bytes[1] == 254;
    }

    private static bool IsUsableGateway(IPAddress address) =>
        !IPAddress.IsLoopback(address)
        && !address.Equals(IPAddress.Any)
        && !address.Equals(IPAddress.IPv6Any)
        && !address.IsIPv6Multicast;
}
