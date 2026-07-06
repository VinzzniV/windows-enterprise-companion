using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wec.Modules.PrintManagement.Domain;

namespace Wec.Modules.PrintManagement.Persistence;

/// <summary>
/// Works against the plain <see cref="DbContext"/> base type so the module never
/// references Infrastructure; the host wires the concrete context (dependency rule 2).
/// </summary>
public sealed class EfPrintSnapshotRepository : IPrintSnapshotRepository
{
    private readonly DbContext _dbContext;
    private readonly ILogger<EfPrintSnapshotRepository> _logger;

    public EfPrintSnapshotRepository(DbContext dbContext, ILogger<EfPrintSnapshotRepository> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task SaveAsync(
        PrintServerSnapshot snapshot, int historyLimit, CancellationToken cancellationToken)
    {
        _dbContext.Set<PrintSnapshotRecord>().Add(new PrintSnapshotRecord
        {
            Server = snapshot.Server,
            CapturedAtUtc = snapshot.CapturedAtUtc,
            PayloadJson = JsonSerializer.Serialize(snapshot),
        });
        await _dbContext.SaveChangesAsync(cancellationToken);

        List<long> staleIds = await _dbContext.Set<PrintSnapshotRecord>()
            .Where(record => record.Server == snapshot.Server)
            .OrderByDescending(record => record.CapturedAtUtc)
            .Skip(historyLimit)
            .Select(record => record.Id)
            .ToListAsync(cancellationToken);
        if (staleIds.Count > 0)
        {
            await _dbContext.Set<PrintSnapshotRecord>()
                .Where(record => staleIds.Contains(record.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<StoredPrintServer>> ListServersAsync(CancellationToken cancellationToken)
    {
        // SQLite cannot aggregate the converted DateTimeOffset column server-side
        var stamps = await _dbContext.Set<PrintSnapshotRecord>()
            .Select(record => new { record.Server, record.CapturedAtUtc })
            .ToListAsync(cancellationToken);
        return [.. stamps
            .GroupBy(stamp => stamp.Server, StringComparer.OrdinalIgnoreCase)
            .Select(group => new StoredPrintServer(
                group.Key,
                group.Max(stamp => stamp.CapturedAtUtc),
                group.Count()))
            .OrderBy(server => server.Server, StringComparer.OrdinalIgnoreCase)];
    }

    public async Task<PrintServerSnapshot?> GetLatestAsync(string server, CancellationToken cancellationToken)
    {
        PrintSnapshotRecord? record = await _dbContext.Set<PrintSnapshotRecord>()
            .Where(snapshot => snapshot.Server == server)
            .OrderByDescending(snapshot => snapshot.CapturedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        return record is null ? null : Deserialize(record);
    }

    public async Task<IReadOnlyList<PrintSnapshotStamp>> GetHistoryAsync(
        string server, CancellationToken cancellationToken) =>
        await _dbContext.Set<PrintSnapshotRecord>()
            .Where(record => record.Server == server)
            .OrderByDescending(record => record.CapturedAtUtc)
            .Select(record => new PrintSnapshotStamp(record.Id, record.CapturedAtUtc))
            .ToListAsync(cancellationToken);

    public async Task<PrintServerSnapshot?> GetByIdAsync(long snapshotId, CancellationToken cancellationToken)
    {
        PrintSnapshotRecord? record = await _dbContext.Set<PrintSnapshotRecord>()
            .FirstOrDefaultAsync(snapshot => snapshot.Id == snapshotId, cancellationToken);
        return record is null ? null : Deserialize(record);
    }

    public Task DeleteServerAsync(string server, CancellationToken cancellationToken) =>
        _dbContext.Set<PrintSnapshotRecord>()
            .Where(record => record.Server == server)
            .ExecuteDeleteAsync(cancellationToken);

    private PrintServerSnapshot? Deserialize(PrintSnapshotRecord record)
    {
        try
        {
            return JsonSerializer.Deserialize<PrintServerSnapshot>(record.PayloadJson);
        }
        catch (JsonException exception)
        {
            // A corrupt snapshot is a miss, not an error
            _logger.LogWarning(exception, "Discarding unreadable print snapshot {SnapshotId}", record.Id);
            return null;
        }
    }
}
