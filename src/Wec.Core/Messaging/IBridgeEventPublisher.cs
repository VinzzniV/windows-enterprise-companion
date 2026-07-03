namespace Wec.Core.Messaging;

/// <summary>
/// Fire-and-forget host → frontend events (ADR 0003). Publishing before the
/// bridge is attached, or after the window closed, is a silent no-op —
/// events are progress sugar, never load-bearing state.
/// </summary>
public interface IBridgeEventPublisher
{
    void Publish(BridgeEvent bridgeEvent);
}
