using Microsoft.Extensions.Options;
using Wec.Core.Dhcp;
using Wec.Core.Network;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.NetworkScan.Domain;

namespace Wec.Modules.NetworkScan.Application;

/// <summary>DHCP server to reconcile reservations against, with the credentials to reach it.</summary>
public sealed record DhcpQuery(string Server, ScanCredentials Credentials);

/// <summary>
/// Orchestrates a network scan: nmap discovery/ports, optional DHCP reservation
/// reconciliation, and device typing. Rows for live hosts carry their reservation
/// state (found → OK, missing → rogue); reserved IPs in the range that answered
/// nothing surface as stale reservations.
/// </summary>
public sealed class NetworkScanService
{
    private readonly INetworkScanner _scanner;
    private readonly IDhcpReader _dhcpReader;
    private readonly NetworkScanOptions _options;

    public NetworkScanService(
        INetworkScanner scanner, IDhcpReader dhcpReader, IOptions<NetworkScanOptions> options)
    {
        _scanner = scanner;
        _dhcpReader = dhcpReader;
        _options = options.Value;
    }

    public async Task<Result<NetworkScanResult>> ScanAsync(
        string target, bool scanPorts, DhcpQuery? dhcp, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.ScanTimeout);

        IReadOnlyList<int> ports = scanPorts ? _options.ScanPorts : [];
        Result<IReadOnlyList<ScannedHost>> scan =
            await _scanner.ScanAsync(_options.NmapPath, target, ports, timeout.Token);
        if (scan.IsFailure)
        {
            return Result.Failure<NetworkScanResult>(scan.Error!);
        }

        IReadOnlyList<DhcpReservation> reservations = [];
        if (dhcp is not null)
        {
            Result<IReadOnlyList<DhcpReservation>> reserved =
                await _dhcpReader.GetReservationsAsync(dhcp.Server, dhcp.Credentials, timeout.Token);
            if (reserved.IsFailure)
            {
                return Result.Failure<NetworkScanResult>(reserved.Error!);
            }

            reservations = reserved.Value;
        }

        var reservationByIp = new Dictionary<string, DhcpReservation>(StringComparer.OrdinalIgnoreCase);
        foreach (DhcpReservation reservation in reservations)
        {
            reservationByIp[reservation.IpAddress] = reservation;
        }

        var liveIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<NetworkHostRow>();
        foreach (ScannedHost host in scan.Value.Where(candidate => candidate.IsUp))
        {
            liveIps.Add(host.IpAddress);
            reservationByIp.TryGetValue(host.IpAddress, out DhcpReservation? reservation);
            rows.Add(new NetworkHostRow(
                host.IpAddress,
                host.Hostname,
                IsUp: true,
                DeviceClassifier.Classify(host),
                host.OpenPorts.Select(port => port.Port).OrderBy(port => port).ToList(),
                host.MacAddress,
                host.MacVendor,
                HasReservation: reservation is not null,
                ReservationName: reservation?.Name));
        }

        if (dhcp is not null)
        {
            ScannedRange range = ScannedRange.Parse(target);
            foreach (DhcpReservation reservation in reservations)
            {
                if (!liveIps.Contains(reservation.IpAddress) && range.Contains(reservation.IpAddress))
                {
                    rows.Add(new NetworkHostRow(
                        reservation.IpAddress, reservation.Name, IsUp: false, DeviceKind.Unknown,
                        [], reservation.MacAddress, MacVendor: null,
                        HasReservation: true, ReservationName: reservation.Name));
                }
            }
        }

        return Result.Success(new NetworkScanResult(target, scanPorts, dhcp is not null, rows));
    }
}
