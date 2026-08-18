namespace Wec.Modules.NetworkScan.Domain;

/// <summary>
/// One row of a network scan. Live hosts carry <see cref="IsUp"/> = true; a
/// reserved-but-dead IP inside the scanned range (a stale DHCP reservation)
/// appears with <see cref="IsUp"/> = false and no ports.
/// </summary>
public sealed record NetworkHostRow(
    string Ip,
    string? Hostname,
    bool IsUp,
    DeviceKind Kind,
    IReadOnlyList<int> OpenPorts,
    string? MacAddress,
    string? MacVendor,
    bool HasReservation,
    string? ReservationName);

public sealed record NetworkScanResult(
    string Target,
    bool PortsScanned,
    bool DhcpChecked,
    IReadOnlyList<NetworkHostRow> Hosts);
