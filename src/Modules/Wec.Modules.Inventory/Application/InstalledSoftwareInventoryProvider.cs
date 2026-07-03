using Wec.Core.Contracts;
using Wec.Modules.Inventory.Persistence;

namespace Wec.Modules.Inventory.Application;

/// <summary>Read contract implementation for consumers outside this module (ADR 0004).</summary>
internal sealed class InstalledSoftwareInventoryProvider : IInstalledSoftwareInventoryProvider
{
    private readonly IHardwareSnapshotRepository _repository;

    public InstalledSoftwareInventoryProvider(IHardwareSnapshotRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<HostInstalledSoftwareData>> GetAllHostsAsync(
        CancellationToken cancellationToken)
    {
        var hosts = new List<HostInstalledSoftwareData>();
        foreach (StoredInventoryHost host in await _repository.ListHostsAsync(cancellationToken))
        {
            CachedHardwareSnapshot? cached = await _repository.GetLatestAsync(host.Host, cancellationToken);
            if (cached?.Snapshot.InstalledSoftware is not { } software)
            {
                continue;
            }

            hosts.Add(new HostInstalledSoftwareData(
                host.Host,
                cached.CapturedAtUtc,
                [.. software.Select(entry => new InstalledSoftwareRecordData(
                    entry.Name, entry.Version, entry.Publisher))]));
        }

        return hosts;
    }
}
