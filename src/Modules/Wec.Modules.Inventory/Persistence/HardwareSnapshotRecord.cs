namespace Wec.Modules.Inventory.Persistence;

public sealed class HardwareSnapshotRecord
{
    public long Id { get; set; }

    public DateTimeOffset CapturedAtUtc { get; set; }

    public string PayloadJson { get; set; } = string.Empty;
}
