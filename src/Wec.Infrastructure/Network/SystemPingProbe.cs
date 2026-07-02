using System.Net.NetworkInformation;
using Microsoft.Extensions.Logging;
using Wec.Core.Abstractions;
using Wec.Core.Results;

namespace Wec.Infrastructure.Network;

public sealed class SystemPingProbe : IPingProbe
{
    private readonly ILogger<SystemPingProbe> _logger;

    public SystemPingProbe(ILogger<SystemPingProbe> logger)
    {
        _logger = logger;
    }

    public async Task<Result<PingProbeReply>> SendAsync(
        string host,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            using var ping = new Ping();
            PingReply reply = await ping.SendPingAsync(host, timeout, cancellationToken: cancellationToken);

            _logger.LogDebug("Ping {Host}: {Status} in {RoundtripMs} ms", host, reply.Status, reply.RoundtripTime);
            return Result.Success(new PingProbeReply(
                reply.Status == IPStatus.Success,
                reply.RoundtripTime,
                reply.Status.ToString()));
        }
        catch (PingException exception)
        {
            _logger.LogWarning(exception, "Ping probe against {Host} failed locally", host);
            return Result.Failure<PingProbeReply>(new Error(
                ErrorCode.NetworkProbeFailed,
                $"The ping probe against '{host}' could not be executed.")
            {
                Details = exception.InnerException?.Message ?? exception.Message,
            });
        }
    }
}
