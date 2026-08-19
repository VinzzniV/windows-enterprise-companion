using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Wec.Core.Abstractions;
using Wec.Core.Results;

namespace Wec.Infrastructure.Network;

public sealed partial class SystemNetworkInfoProvider : INetworkInfoProvider
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
            uint? preferredIpv4InterfaceIndex = GetPreferredIpv4InterfaceIndex();
            var adapters = NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up
                    && adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback
                    && adapter.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .Select(adapter => ToAdapterInfo(adapter, preferredIpv4InterfaceIndex))
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

    private static NetworkAdapterInfo ToAdapterInfo(NetworkInterface adapter, uint? preferredIpv4InterfaceIndex)
    {
        IPInterfaceProperties properties = adapter.GetIPProperties();
        int? interfaceIndex = ReadInterfaceIndex(properties);

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
            adapter.NetworkInterfaceType.ToString(),
            interfaceIndex,
            preferredIpv4InterfaceIndex is null || interfaceIndex is null
                ? null
                : interfaceIndex.Value == preferredIpv4InterfaceIndex.Value);
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

    private static int? ReadInterfaceIndex(IPInterfaceProperties properties)
    {
        try
        {
            return properties.GetIPv4Properties()?.Index;
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }

    private uint? GetPreferredIpv4InterfaceIndex()
    {
        // This asks the Windows route table which interface it would use for a
        // normal external IPv4 destination. No packet is sent.
        byte[] addressBytes = [1, 1, 1, 1];
        var destination = new SockaddrIn
        {
            Family = (short)AddressFamily.InterNetwork,
            Address = BitConverter.ToUInt32(addressBytes),
            Padding = new byte[8],
        };

        uint error = GetBestInterfaceEx(ref destination, out uint interfaceIndex);
        if (error == 0)
        {
            return interfaceIndex;
        }

        LogPreferredRouteUnavailable(error);
        return null;
    }

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Windows could not identify the preferred IPv4 route interface (error {ErrorCode})")]
    private partial void LogPreferredRouteUnavailable(uint errorCode);

    [StructLayout(LayoutKind.Sequential)]
    private struct SockaddrIn
    {
        public short Family;
        public ushort Port;
        public uint Address;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] Padding;
    }

    [DllImport("iphlpapi.dll")]
    private static extern uint GetBestInterfaceEx(ref SockaddrIn destinationAddress, out uint bestInterfaceIndex);
}
