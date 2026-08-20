namespace Wec.Core.Contracts;

public sealed record InventoryClientSnapshotHost(
    string Host,
    DateTimeOffset CapturedAtUtc);

public interface IInventoryClientSnapshotProvider
{
    Task<IReadOnlyList<InventoryClientSnapshotHost>> ListHostsAsync(CancellationToken cancellationToken);
}

public sealed record SavedClientTarget(
    string Label,
    string Host);

public interface ISavedClientTargetProvider
{
    Task<IReadOnlyList<SavedClientTarget>> ListClientsAsync(CancellationToken cancellationToken);
}
