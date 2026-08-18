using Wec.Core.Results;
using Wec.Core.Targets;

namespace Wec.Core.Dhcp;

/// <summary>One IPv4 DHCP reservation (IP ↔ client, per scope) read from a DHCP server.</summary>
public sealed record DhcpReservation(string IpAddress, string? MacAddress, string? Name);

/// <summary>Reads IPv4 reservations from a Windows DHCP server, read-only.</summary>
public interface IDhcpReader
{
    Task<Result<IReadOnlyList<DhcpReservation>>> GetReservationsAsync(
        string dhcpServer, ScanCredentials credentials, CancellationToken cancellationToken);
}
