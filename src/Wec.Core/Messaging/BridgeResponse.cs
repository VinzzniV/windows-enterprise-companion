using Wec.Core.Results;

namespace Wec.Core.Messaging;

public sealed record BridgeResponse(string Id, bool Success, object? Data, Error? Error)
{
    public static BridgeResponse ForSuccess(string id, object? data) => new(id, true, data, null);

    public static BridgeResponse ForFailure(string id, Error error) => new(id, false, null, error);
}
