using Wec.Core.Abstractions;
using Wec.Core.Messaging;
using Wec.Core.Results;

namespace Wec.Host.Bridge;

public sealed record PingRequest;

public sealed record PingResponse(string Message, DateTimeOffset Timestamp);

internal sealed class PingHandler : IActionHandler<PingRequest, PingResponse>
{
    private readonly IClock _clock;

    public PingHandler(IClock clock)
    {
        _clock = clock;
    }

    public string Module => "system";

    public string Action => "ping";

    public Task<Result<PingResponse>> HandleAsync(PingRequest payload, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success(new PingResponse("pong", _clock.UtcNow)));
}
