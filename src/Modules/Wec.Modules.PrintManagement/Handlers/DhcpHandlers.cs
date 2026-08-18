using Microsoft.Extensions.Options;
using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.PrintManagement.Application;

namespace Wec.Modules.PrintManagement.Handlers;

public sealed record GetNetworkPolicyRequest;

/// <summary>The network placement policy the UI classifies printer IPs against.</summary>
public sealed record NetworkPolicyResult(
    IReadOnlyList<string> PrinterSubnets, IReadOnlyList<string> LegacySubnets, string? DhcpServer);

internal sealed class GetNetworkPolicyHandler : IActionHandler<GetNetworkPolicyRequest, NetworkPolicyResult>
{
    private readonly PrintManagementOptions _options;

    public GetNetworkPolicyHandler(IOptions<PrintManagementOptions> options)
    {
        _options = options.Value;
    }

    public string Module => "printmanagement";

    public string Action => "getNetworkPolicy";

    public Task<Result<NetworkPolicyResult>> HandleAsync(
        GetNetworkPolicyRequest payload, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success(new NetworkPolicyResult(
            _options.PrinterSubnets, _options.LegacyPrinterSubnets, _options.DhcpServer)));
}

/// <summary>
/// <c>Target.Host</c> is the DHCP server; its optional credentials let the query
/// run as the admin the DHCP server (usually a domain controller) requires,
/// without elevating the app itself.
/// </summary>
public sealed record CheckDhcpRequest(TargetRequest Target, IReadOnlyList<string> Ips);

internal sealed class CheckDhcpHandler : IActionHandler<CheckDhcpRequest, DhcpCheckResult>
{
    private readonly DhcpCheckService _service;

    public CheckDhcpHandler(DhcpCheckService service)
    {
        _service = service;
    }

    public string Module => "printmanagement";

    public string Action => "checkDhcp";

    public Task<Result<DhcpCheckResult>> HandleAsync(
        CheckDhcpRequest payload, CancellationToken cancellationToken)
    {
        TargetRequest target = payload.Target ?? new TargetRequest();
        if (string.IsNullOrWhiteSpace(target.Host))
        {
            return Task.FromResult(Result.Failure<DhcpCheckResult>(new Error(
                ErrorCode.InvalidRequest, "A DHCP server is required to check reservations.")));
        }

        Result<ScanCredentials> credentials = target.ToScanCredentials();
        if (credentials.IsFailure)
        {
            return Task.FromResult(Result.Failure<DhcpCheckResult>(credentials.Error!));
        }

        return _service.CheckAsync(target.Host!.Trim(), credentials.Value, payload.Ips ?? [], cancellationToken);
    }
}
