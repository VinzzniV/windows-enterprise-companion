using System.Text.Json;

namespace Wec.Core.Messaging;

public sealed record BridgeRequest(string Id, string Module, string Action, JsonElement? Payload);
