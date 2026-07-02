using Wec.Core.Results;

namespace Wec.Core.Abstractions;

/// <summary>
/// A non-success reply (timeout, unreachable) is a successful probe with
/// <see cref="Success"/> false — only local errors (invalid target, no
/// permission, stack failure) surface as Result failures.
/// </summary>
public sealed record PingProbeReply(bool Success, long RoundtripMilliseconds, string Status);

public interface IPingProbe
{
    Task<Result<PingProbeReply>> SendAsync(string host, TimeSpan timeout, CancellationToken cancellationToken);
}
