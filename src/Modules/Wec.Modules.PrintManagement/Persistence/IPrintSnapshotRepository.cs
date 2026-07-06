using Wec.Modules.PrintManagement.Domain;

namespace Wec.Modules.PrintManagement.Persistence;

public sealed record StoredPrintServer(string Server, DateTimeOffset CapturedAtUtc, int SnapshotCount);

public sealed record PrintSnapshotStamp(long Id, DateTimeOffset CapturedAtUtc);

/// <summary>
/// Snapshot store WITH history (ADR 0009) — the lease diff needs points in
/// time, unlike the hardware inventory's latest-only cache.
/// </summary>
public interface IPrintSnapshotRepository
{
    /// <summary>Appends a snapshot and prunes the oldest beyond <paramref name="historyLimit"/> per server.</summary>
    Task SaveAsync(PrintServerSnapshot snapshot, int historyLimit, CancellationToken cancellationToken);

    Task<IReadOnlyList<StoredPrintServer>> ListServersAsync(CancellationToken cancellationToken);

    Task<PrintServerSnapshot?> GetLatestAsync(string server, CancellationToken cancellationToken);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<PrintSnapshotStamp>> GetHistoryAsync(string server, CancellationToken cancellationToken);

    Task<PrintServerSnapshot?> GetByIdAsync(long snapshotId, CancellationToken cancellationToken);

    Task DeleteServerAsync(string server, CancellationToken cancellationToken);
}
