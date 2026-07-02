using Wec.Modules.Inventory.Domain;

namespace Wec.Modules.Inventory.Application;

public sealed record HardwareInfoResult(
    HardwareSnapshot Snapshot,
    DateTimeOffset CapturedAtUtc,
    bool FromCache);
