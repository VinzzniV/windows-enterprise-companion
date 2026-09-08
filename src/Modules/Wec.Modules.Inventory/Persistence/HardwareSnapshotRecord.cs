namespace Wec.Modules.Inventory.Persistence;

public sealed class HardwareSnapshotRecord
{
    public long Id { get; set; }

    public string Host { get; set; } = string.Empty;

    public string? IdentityKey { get; set; }

    public DateTimeOffset CapturedAtUtc { get; set; }

    public string PayloadJson { get; set; } = string.Empty;
}
