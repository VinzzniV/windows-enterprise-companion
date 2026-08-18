using Microsoft.Extensions.Options;
using Wec.Core.Dhcp;
using Wec.Core.Results;
using Wec.Core.Targets;

namespace Wec.Modules.PrintManagement.Application;

/// <summary>One reserved device address, with the reservation's client id and name.</summary>
public sealed record DhcpReservationInfo(string Ip, string? Mac, string? Name);

/// <summary>The reservations that matched the requested printer addresses.</summary>
public sealed record DhcpCheckResult(IReadOnlyList<DhcpReservationInfo> Reserved);

/// <summary>
/// Looks up which of the given printer addresses have a DHCP reservation. The
/// query itself is delegated to <see cref="IDhcpReader"/>; this service only
/// bounds it with the configured timeout and keeps the matches by IP.
/// </summary>
public sealed class DhcpCheckService
{
    private readonly IDhcpReader _reader;
    private readonly PrintManagementOptions _options;

    public DhcpCheckService(IDhcpReader reader, IOptions<PrintManagementOptions> options)
    {
        _reader = reader;
        _options = options.Value;
    }

    public async Task<Result<DhcpCheckResult>> CheckAsync(
        string dhcpServer,
        ScanCredentials credentials,
        IReadOnlyList<string> ips,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.DhcpQueryTimeout);

        Result<IReadOnlyList<DhcpReservation>> reservations =
            await _reader.GetReservationsAsync(dhcpServer, credentials, timeout.Token);
        if (reservations.IsFailure)
        {
            return Result.Failure<DhcpCheckResult>(reservations.Error!);
        }

        var wanted = new HashSet<string>(ips, StringComparer.OrdinalIgnoreCase);
        List<DhcpReservationInfo> matched = [.. reservations.Value
            .Where(reservation => wanted.Contains(reservation.IpAddress))
            .Select(reservation => new DhcpReservationInfo(
                reservation.IpAddress, reservation.MacAddress, reservation.Name))];
        return Result.Success(new DhcpCheckResult(matched));
    }
}
