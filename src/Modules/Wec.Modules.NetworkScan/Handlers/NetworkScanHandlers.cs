using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.NetworkScan.Application;
using Wec.Modules.NetworkScan.Domain;

namespace Wec.Modules.NetworkScan.Handlers;

/// <summary>
/// <c>Target</c> is the free-form nmap target (CIDR, octet range, single IP or a
/// space-separated list). <c>Dhcp</c> is optional: when its Host is set, DHCP
/// reservations are joined in — its credentials let the query run as the admin
/// the DHCP server (usually a domain controller) requires, without elevating WEC.
/// </summary>
public sealed record ScanNetworkRequest(string? Target, bool ScanPorts, TargetRequest? Dhcp);

internal sealed class ScanNetworkHandler : IActionHandler<ScanNetworkRequest, NetworkScanResult>
{
    private readonly NetworkScanService _service;

    public ScanNetworkHandler(NetworkScanService service)
    {
        _service = service;
    }

    public string Module => "networkscan";

    public string Action => "scan";

    public Task<Result<NetworkScanResult>> HandleAsync(
        ScanNetworkRequest payload, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payload.Target))
        {
            return Task.FromResult(Result.Failure<NetworkScanResult>(new Error(
                ErrorCode.InvalidRequest, "A scan target (e.g. 172.20.20.0/24) is required.")));
        }

        DhcpQuery? dhcp = null;
        if (payload.Dhcp is { } request && !string.IsNullOrWhiteSpace(request.Host))
        {
            Result<ScanCredentials> credentials = request.ToScanCredentials();
            if (credentials.IsFailure)
            {
                return Task.FromResult(Result.Failure<NetworkScanResult>(credentials.Error!));
            }

            dhcp = new DhcpQuery(request.Host!.Trim(), credentials.Value);
        }

        return _service.ScanAsync(payload.Target!.Trim(), payload.ScanPorts, dhcp, cancellationToken);
    }
}
