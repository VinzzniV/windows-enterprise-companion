using System.Text.Json.Serialization;

namespace Wec.Core.Messaging;

public sealed record BridgeEvent(
    string Module,
    [property: JsonPropertyName("event")] string EventName,
    object? Payload)
{
    public string Type { get; } = "event";
}
