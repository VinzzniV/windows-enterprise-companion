using Wec.Modules.Inventory.Domain;

namespace Wec.Modules.Inventory.Application;

public sealed record HardwareInfoResult(
    string Host,
    HardwareSnapshot Snapshot,
    DateTimeOffset CapturedAtUtc,
    bool FromCache);
