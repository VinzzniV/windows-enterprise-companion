using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wec.Modules.Inventory.Domain;

namespace Wec.Modules.Inventory.Persistence;

/// <summary>
/// Works against the plain <see cref="DbContext"/> base type so the module never
/// references Infrastructure; the host wires the concrete context (dependency rule 2).
/// </summary>
public sealed class EfHardwareSnapshotRepository : IHardwareSnapshotRepository
{
    private readonly DbContext _dbContext;
    private readonly ILogger<EfHardwareSnapshotRepository> _logger;

    public EfHardwareSnapshotRepository(DbContext dbContext, ILogger<EfHardwareSnapshotRepository> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<CachedHardwareSnapshot?> GetLatestAsync(string hostKey, CancellationToken cancellationToken)
    {
        HardwareSnapshotRecord? record = await _dbContext.Set<HardwareSnapshotRecord>()
            .Where(snapshot => snapshot.Host == hostKey)
            .OrderByDescending(snapshot => snapshot.CapturedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (record is null)
        {
            return null;
        }

        try
        {
            HardwareSnapshot? snapshot = JsonSerializer.Deserialize<HardwareSnapshot>(record.PayloadJson);
            return snapshot is null ? null : new CachedHardwareSnapshot(snapshot, record.CapturedAtUtc);
        }
        catch (JsonException exception)
        {
            // A corrupt cache entry is a cache miss, not an error
            _logger.LogWarning(exception, "Discarding unreadable hardware snapshot {SnapshotId}", record.Id);
            return null;
        }
    }

    public async Task<IReadOnlyList<StoredInventoryHost>> ListHostsAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.Set<HardwareSnapshotRecord>()
            .AsNoTracking()
            .Where(snapshot => snapshot.Host.Trim() != string.Empty)
            .OrderBy(snapshot => snapshot.Host)
            .Select(snapshot => new StoredInventoryHost(snapshot.Host, snapshot.CapturedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public Task DeleteAsync(string hostKey, CancellationToken cancellationToken) =>
        _dbContext.Set<HardwareSnapshotRecord>()
            .Where(snapshot => snapshot.Host == hostKey)
            .ExecuteDeleteAsync(cancellationToken);

    public async Task SaveAsync(
        string hostKey,
        HardwareSnapshot snapshot,
        DateTimeOffset capturedAtUtc,
        CancellationToken cancellationToken)
    {
        // One cache entry per host; replace instead of accumulating history
        await _dbContext.Set<HardwareSnapshotRecord>()
            .Where(existing => existing.Host == hostKey)
            .ExecuteDeleteAsync(cancellationToken);

        _dbContext.Set<HardwareSnapshotRecord>().Add(new HardwareSnapshotRecord
        {
            Host = hostKey,
            CapturedAtUtc = capturedAtUtc,
            PayloadJson = JsonSerializer.Serialize(snapshot),
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
