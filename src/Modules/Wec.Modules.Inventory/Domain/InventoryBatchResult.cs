using Wec.Core.Targets;
using Wec.Modules.Inventory.Application;

namespace Wec.Modules.Inventory.Domain;

public enum InventoryBatchHostStatus
{
    Queued = 0,
    Running,
    Completed,
    Failed,
}

public sealed record InventoryBatchHostOutcome(
    string Host,
    InventoryBatchHostStatus Status,
    HardwareInfoResult? Inventory,
    ScanError? Error);

public sealed record InventoryBatchResult(
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    IReadOnlyList<InventoryBatchHostOutcome> Hosts);
