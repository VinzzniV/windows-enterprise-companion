using Wec.Core.Results;

namespace Wec.Core.Abstractions;

public sealed record Ipv4AddressInfo(string Address, int PrefixLength);

public sealed record NetworkAdapterInfo(
    string Name,
    string Description,
    IReadOnlyList<Ipv4AddressInfo> Ipv4Addresses,
    IReadOnlyList<string> GatewayAddresses,
    IReadOnlyList<string> DnsServers);

public interface INetworkInfoProvider
{
    /// <summary>Operational (up) non-loopback, non-tunnel adapters.</summary>
    Result<IReadOnlyList<NetworkAdapterInfo>> GetActiveAdapters();
}
