using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Wec.Core.Abstractions;
using Wec.Core.Results;

namespace Wec.Infrastructure.Network;

public sealed class SystemNetworkInfoProvider : INetworkInfoProvider
{
    private readonly ILogger<SystemNetworkInfoProvider> _logger;

    public SystemNetworkInfoProvider(ILogger<SystemNetworkInfoProvider> logger)
    {
        _logger = logger;
    }

    public Result<IReadOnlyList<NetworkAdapterInfo>> GetActiveAdapters()
    {
        try
        {
            var adapters = NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up
                    && adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback
                    && adapter.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .Select(ToAdapterInfo)
                .ToList();

            return Result.Success<IReadOnlyList<NetworkAdapterInfo>>(adapters);
        }
        catch (NetworkInformationException exception)
        {
            _logger.LogError(exception, "Reading network interfaces failed");
            return Result.Failure<IReadOnlyList<NetworkAdapterInfo>>(new Error(
                ErrorCode.NetworkProbeFailed,
                "The network interface information could not be read.")
            {
                Details = exception.Message,
            });
        }
    }

    private static NetworkAdapterInfo ToAdapterInfo(NetworkInterface adapter)
    {
        IPInterfaceProperties properties = adapter.GetIPProperties();

        var ipv4Addresses = properties.UnicastAddresses
            .Where(unicast => unicast.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(unicast => new Ipv4AddressInfo(unicast.Address.ToString(), unicast.PrefixLength))
            .ToList();

        var gateways = properties.GatewayAddresses
            .Select(gateway => gateway.Address.ToString())
            .Where(address => !string.IsNullOrEmpty(address))
            .ToList();

        var dnsServers = properties.DnsAddresses
            .Select(dns => dns.ToString())
            .ToList();

        return new NetworkAdapterInfo(
            adapter.Name,
            adapter.Description,
            ipv4Addresses,
            gateways,
            dnsServers,
            FormatMacAddress(adapter),
            adapter.Speed > 0 ? adapter.Speed : null,
            ReadDhcpEnabled(properties),
            adapter.NetworkInterfaceType.ToString());
    }

    private static string? FormatMacAddress(NetworkInterface adapter)
    {
        byte[] addressBytes = adapter.GetPhysicalAddress().GetAddressBytes();
        return addressBytes.Length == 0
            ? null
            : string.Join(":", addressBytes.Select(part => part.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static bool? ReadDhcpEnabled(IPInterfaceProperties properties)
    {
        try
        {
            return properties.GetIPv4Properties()?.IsDhcpEnabled;
        }
        catch (NetworkInformationException)
        {
            // IPv6-only adapters have no IPv4 properties
            return null;
        }
    }
}
