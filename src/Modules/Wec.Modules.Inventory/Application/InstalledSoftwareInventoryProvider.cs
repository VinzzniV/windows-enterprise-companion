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

    public async Task<InstalledSoftwareSnapshotData?> GetLatestAsync(
        string? host,
        CancellationToken cancellationToken)
    {
        string cacheKey = host is null
            ? Wec.Core.Targets.ScanTarget.Local.CacheKey
            : Wec.Core.Targets.ScanTarget.Remote(host).CacheKey;
        CachedHardwareSnapshot? cached = await _repository.GetLatestAsync(cacheKey, cancellationToken);
        if (cached is null)
        {
            return null;
        }

        return new InstalledSoftwareSnapshotData(
            cacheKey,
            cached.CapturedAtUtc,
            IsComplete: cached.Snapshot.InstalledSoftware is not null,
            MapSoftware(cached),
            cached.Snapshot.InstalledSoftwareError?.Code,
            cached.Snapshot.InstalledSoftwareError?.Message);
    }

    public async Task<IReadOnlyList<HostInstalledSoftwareData>> GetAllHostsAsync(
        CancellationToken cancellationToken)
    {
        var hosts = new List<HostInstalledSoftwareData>();
        foreach (StoredInventoryHost host in await _repository.ListHostsAsync(cancellationToken))
        {
            CachedHardwareSnapshot? cached = await _repository.GetLatestAsync(host.Host, cancellationToken);
            if (cached?.Snapshot.InstalledSoftware is null)
            {
                continue;
            }

            hosts.Add(new HostInstalledSoftwareData(
                host.Host,
                cached.CapturedAtUtc,
                MapSoftware(cached)));
        }

        return hosts;
    }

    private static IReadOnlyList<InstalledSoftwareRecordData> MapSoftware(CachedHardwareSnapshot cached) =>
        cached.Snapshot.InstalledSoftware is not { } software
            ? []
            : [.. software.Select(entry => new InstalledSoftwareRecordData(
                entry.Name, entry.Version, entry.Publisher))];
}
