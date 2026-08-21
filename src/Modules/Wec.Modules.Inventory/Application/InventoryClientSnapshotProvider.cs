using Wec.Core.Contracts;
using Wec.Modules.Inventory.Persistence;

namespace Wec.Modules.Inventory.Application;

internal sealed class InventoryClientSnapshotProvider(
    IHardwareSnapshotRepository repository) : IInventoryClientSnapshotProvider
{
    public async Task<IReadOnlyList<InventoryClientSnapshotHost>> ListHostsAsync(
        CancellationToken cancellationToken) =>
        (await repository.ListHostsAsync(cancellationToken))
            .Select(host => new InventoryClientSnapshotHost(host.Host, host.CapturedAtUtc))
            .ToList();
}
