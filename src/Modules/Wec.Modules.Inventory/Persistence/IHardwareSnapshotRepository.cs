using Wec.Modules.Inventory.Domain;

namespace Wec.Modules.Inventory.Persistence;

public sealed record CachedHardwareSnapshot(HardwareSnapshot Snapshot, DateTimeOffset CapturedAtUtc);

public sealed record StoredInventoryHost(string Host, DateTimeOffset CapturedAtUtc);

public interface IHardwareSnapshotRepository
{
    Task<CachedHardwareSnapshot?> GetLatestAsync(string hostKey, CancellationToken cancellationToken);

    Task SaveAsync(
        string hostKey,
        HardwareSnapshot snapshot,
        DateTimeOffset capturedAtUtc,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StoredInventoryHost>> ListHostsAsync(CancellationToken cancellationToken);

    Task DeleteAsync(string hostKey, CancellationToken cancellationToken);
}
