using Wec.Modules.Inventory.Domain;

namespace Wec.Modules.Inventory.Persistence;

public sealed record CachedHardwareSnapshot(HardwareSnapshot Snapshot, DateTimeOffset CapturedAtUtc);

public interface IHardwareSnapshotRepository
{
    Task<CachedHardwareSnapshot?> GetLatestAsync(CancellationToken cancellationToken);

    Task SaveAsync(HardwareSnapshot snapshot, DateTimeOffset capturedAtUtc, CancellationToken cancellationToken);
}
