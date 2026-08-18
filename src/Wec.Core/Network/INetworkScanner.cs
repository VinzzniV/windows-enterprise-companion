using Wec.Core.Results;

namespace Wec.Core.Network;

public sealed record ScannedPort(int Port, string? Service);

public sealed record ScannedHost(
    string IpAddress,
    bool IsUp,
    string? Hostname,
    string? MacAddress,
    string? MacVendor,
    IReadOnlyList<ScannedPort> OpenPorts);

/// <summary>
/// Active network discovery via nmap. Read-only: sweeps a target (CIDR, range,
/// single IP or space-separated list) for live hosts, their reverse-DNS name,
/// on-link MAC/vendor and — when <paramref name="ports"/> is non-empty — open
/// TCP ports. An empty port list means host discovery only. No writes, ever.
/// </summary>
public interface INetworkScanner
{
    Task<Result<IReadOnlyList<ScannedHost>>> ScanAsync(
        string? nmapPath, string target, IReadOnlyList<int> ports, CancellationToken cancellationToken);
}
