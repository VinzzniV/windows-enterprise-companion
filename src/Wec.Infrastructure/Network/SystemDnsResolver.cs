using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Wec.Core.Abstractions;
using Wec.Core.Results;

namespace Wec.Infrastructure.Network;

public sealed class SystemDnsResolver : IDnsResolver
{
    private readonly ILogger<SystemDnsResolver> _logger;

    public SystemDnsResolver(ILogger<SystemDnsResolver> logger)
    {
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<string>>> ResolveAsync(
        string hostname,
        CancellationToken cancellationToken)
    {
        try
        {
            IPAddress[] addresses = await Dns.GetHostAddressesAsync(hostname, cancellationToken);
            return Result.Success<IReadOnlyList<string>>(
                addresses.Select(address => address.ToString()).ToList());
        }
        catch (SocketException exception)
        {
            _logger.LogWarning(exception, "DNS resolution of {Hostname} failed", hostname);
            return Result.Failure<IReadOnlyList<string>>(new Error(
                ErrorCode.NetworkProbeFailed,
                $"DNS resolution of '{hostname}' failed.")
            {
                Details = $"{exception.SocketErrorCode}: {exception.Message}",
            });
        }
    }
}
