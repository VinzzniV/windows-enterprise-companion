namespace Wec.Core.Messaging;

/// <summary>
/// Marks a bridge payload that is not reachable from an action handler, such
/// as an event payload, so the TypeScript contract generator includes it.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum)]
public sealed class BridgeContractAttribute : Attribute;
