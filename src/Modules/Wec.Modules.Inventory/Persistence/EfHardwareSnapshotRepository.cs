using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wec.Core.Targets;
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
        string identityKey = DeviceIdentity.NormalizeHost(hostKey);
        HardwareSnapshotRecord? record = await _dbContext.Set<HardwareSnapshotRecord>()
            .AsNoTracking()
            .Where(snapshot => snapshot.IdentityKey == identityKey
                || (snapshot.IdentityKey == null
                    && EF.Functions.Collate(snapshot.Host.Trim(), "NOCASE") == identityKey))
            .OrderByDescending(snapshot => snapshot.CapturedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (record is null)
        {
            return null;
        }

        try
        {
            HardwareSnapshot? snapshot = JsonSerializer.Deserialize<HardwareSnapshot>(record.PayloadJson);
            return snapshot is null
                ? throw new InvalidDataException($"Stored hardware snapshot {record.Id} contains no payload.")
                : new CachedHardwareSnapshot(snapshot, record.CapturedAtUtc);
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Stored hardware snapshot {SnapshotId} is unreadable", record.Id);
            throw new InvalidDataException($"Stored hardware snapshot {record.Id} is unreadable.", exception);
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

    public Task DeleteAsync(string hostKey, CancellationToken cancellationToken)
    {
        string identityKey = DeviceIdentity.NormalizeHost(hostKey);
        return _dbContext.Set<HardwareSnapshotRecord>()
            .Where(snapshot => snapshot.IdentityKey == identityKey
                || (snapshot.IdentityKey == null
                    && EF.Functions.Collate(snapshot.Host.Trim(), "NOCASE") == identityKey))
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task SaveAsync(
        string hostKey,
        HardwareSnapshot snapshot,
        DateTimeOffset capturedAtUtc,
        CancellationToken cancellationToken)
    {
        string identityKey = DeviceIdentity.NormalizeHost(hostKey);
        string payloadJson = JsonSerializer.Serialize(snapshot);
        long capturedAtUtcTicks = capturedAtUtc.ToUniversalTime().Ticks;
        await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO inventory_hardware_snapshots (host, identity_key, captured_at_utc, payload_json)
            VALUES ({identityKey}, {identityKey}, {capturedAtUtcTicks}, {payloadJson})
            ON CONFLICT(identity_key) WHERE identity_key IS NOT NULL
            DO UPDATE SET
                host = excluded.host,
                captured_at_utc = excluded.captured_at_utc,
                payload_json = excluded.payload_json
            WHERE excluded.captured_at_utc >= inventory_hardware_snapshots.captured_at_utc
            """, cancellationToken);
    }
}
