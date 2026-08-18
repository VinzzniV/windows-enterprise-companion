using Wec.Core.Network;
using Wec.Modules.NetworkScan.Domain;

namespace Wec.Modules.NetworkScan.Application;

/// <summary>
/// Best-effort device type from open ports, service banners and MAC vendor.
/// No OS fingerprint (that needs raw packets / elevation, ADR 0002); open ports
/// are the strongest functional signal, the MAC vendor a fallback. Ambiguous
/// hosts stay <see cref="DeviceKind.Unknown"/> rather than guessing wrong.
/// ponytail: substring vendor match, no OUI database — good enough until it isn't.
/// </summary>
public static class DeviceClassifier
{
    private const int Jetdirect = 9100, Lpd = 515, Ipp = 631, Snmp = 161, Telnet = 23,
        Rdp = 3389, Smb = 445, Ssh = 22, Http = 80, Https = 443;

    private static readonly string[] PrinterVendors =
        ["hewlett", "canon", "kyocera", "brother", "xerox", "ricoh", "lexmark", "epson",
         "konica", "oki", "sharp", "utax", "develop", "zebra"];

    // Checked before printer vendors so "Hewlett Packard Enterprise" reads as a switch, not a printer.
    private static readonly string[] NetworkVendors =
        ["cisco", "aruba", "juniper", "ubiquiti", "mikrotik", "netgear", "tp-link", "tplink",
         "fortinet", "zyxel", "extreme", "ruckus", "allied telesis", "lancom", "hpe",
         "hewlett packard enterprise", "d-link", "huawei"];

    private static readonly string[] ComputerVendors =
        ["dell", "lenovo", "microsoft", "asus", "gigabyte", "msi", "vmware", "fujitsu",
         "acer", "apple", "intel", "supermicro"];

    public static DeviceKind Classify(ScannedHost host)
    {
        var ports = host.OpenPorts.Select(port => port.Port).ToHashSet();

        bool HasService(string token) => host.OpenPorts.Any(
            port => port.Service is { } service && service.Contains(token, StringComparison.OrdinalIgnoreCase));

        if (ports.Contains(Jetdirect) || ports.Contains(Lpd) || ports.Contains(Ipp)
            || HasService("jetdirect") || HasService("printer") || HasService("ipp") || HasService("pdl"))
        {
            return DeviceKind.Printer;
        }

        if (ports.Contains(Rdp) || ports.Contains(Smb))
        {
            return DeviceKind.Computer;
        }

        if (ports.Contains(Snmp)
            && (ports.Contains(Telnet) || ports.Contains(Ssh) || ports.Contains(Http) || ports.Contains(Https)))
        {
            return DeviceKind.NetworkDevice;
        }

        return ClassifyByVendor(host.MacVendor);
    }

    private static DeviceKind ClassifyByVendor(string? vendor)
    {
        if (string.IsNullOrWhiteSpace(vendor))
        {
            return DeviceKind.Unknown;
        }

        string value = vendor.ToLowerInvariant();
        if (NetworkVendors.Any(value.Contains))
        {
            return DeviceKind.NetworkDevice;
        }

        if (PrinterVendors.Any(value.Contains))
        {
            return DeviceKind.Printer;
        }

        if (ComputerVendors.Any(value.Contains))
        {
            return DeviceKind.Computer;
        }

        return DeviceKind.Unknown;
    }
}
