using Wec.Modules.Inventory.Domain;

namespace Wec.Modules.Inventory.Persistence;

public sealed record CachedHardwareSnapshot(HardwareSnapshot Snapshot, DateTimeOffset CapturedAtUtc);

public sealed record StoredInventoryHost(string Host, DateTimeOffset CapturedAtUtc);

public sealed record StoredInventoryUserEvidence(
    long SnapshotId, string Host, DateTimeOffset CapturedAtUtc, bool Readable, DeviceUserEvidence? Evidence);

public sealed record StoredInventoryUserEvidenceBatch(
    int StoredHostCount, int LatestRecordCount, IReadOnlyList<StoredInventoryUserEvidence> Records);

public interface IHardwareSnapshotRepository
{
    Task<CachedHardwareSnapshot?> GetLatestAsync(string hostKey, CancellationToken cancellationToken);

    Task SaveAsync(
        string hostKey,
        HardwareSnapshot snapshot,
        DateTimeOffset capturedAtUtc,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StoredInventoryHost>> ListHostsAsync(CancellationToken cancellationToken);

    Task<StoredInventoryUserEvidenceBatch> GetLatestUserEvidenceBatchAsync(
        int maximumRecords, CancellationToken cancellationToken);

    Task DeleteAsync(string hostKey, CancellationToken cancellationToken);
}
