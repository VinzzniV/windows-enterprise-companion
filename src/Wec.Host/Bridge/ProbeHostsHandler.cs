using System.Net.Sockets;
using Wec.Core.Abstractions;
using Wec.Core.Messaging;
using Wec.Core.Results;

namespace Wec.Host.Bridge;

public sealed record ProbeHostsRequest(IReadOnlyList<string>? Hosts);

public sealed record HostProbeResult(string Host, bool Reachable, bool Manageable);

public sealed record ProbeHostsResponse(IReadOnlyList<HostProbeResult> Results);

/// <summary>
/// Liveness probe for the Clients list: ICMP ping ("reachable") plus a TCP 5985
/// connect ("manageable" over WinRM). The UI sends only the visible/filtered
/// hosts. This never scans and needs no credentials.
/// </summary>
internal sealed class ProbeHostsHandler : IActionHandler<ProbeHostsRequest, ProbeHostsResponse>
{
    private const int WinRmPort = 5985;
    private const int MaxHostsPerCall = 512;
    private const int MaxParallelism = 32;
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(800);

    private readonly IPingProbe _pingProbe;

    public ProbeHostsHandler(IPingProbe pingProbe) => _pingProbe = pingProbe;

    public string Module => "connectivity";

    public string Action => "probeHosts";

    public async Task<Result<ProbeHostsResponse>> HandleAsync(
        ProbeHostsRequest payload, CancellationToken cancellationToken)
    {
        string[] hosts = (payload.Hosts ?? [])
            .Select(host => host?.Trim() ?? string.Empty)
            .Where(host => host.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxHostsPerCall)
            .ToArray();

        var results = new HostProbeResult[hosts.Length];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, hosts.Length),
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelism, CancellationToken = cancellationToken },
            async (index, ct) =>
            {
                string host = hosts[index];
                Task<bool> reachable = ReachableAsync(host, ct);
                Task<bool> manageable = IsPortOpenAsync(host, WinRmPort, ProbeTimeout, ct);
                await Task.WhenAll(reachable, manageable);
                results[index] = new HostProbeResult(host, reachable.Result, manageable.Result);
            });

        return Result.Success(new ProbeHostsResponse(results));
    }

    private async Task<bool> ReachableAsync(string host, CancellationToken cancellationToken)
    {
        Result<PingProbeReply> reply = await _pingProbe.SendAsync(host, ProbeTimeout, cancellationToken);
        return reply.IsSuccess && reply.Value.Success;
    }

    // ponytail: a raw TCP connect with a timeout is the whole "is WinRM listening" test.
    internal static async Task<bool> IsPortOpenAsync(
        string host, int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);
            await client.ConnectAsync(host, port, cts.Token);
            return client.Connected;
        }
        catch
        {
            // Any failure (refused, timeout, DNS) means "not manageable" — never an error.
            return false;
        }
    }
}
