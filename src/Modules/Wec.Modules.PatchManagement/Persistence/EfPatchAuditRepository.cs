using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Wec.Modules.PatchManagement.Persistence;

public sealed class EfPatchAuditRepository : IPatchAuditRepository
{
    private readonly DbContext _dbContext;

    public EfPatchAuditRepository(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(PatchAuditEntry entry, CancellationToken cancellationToken)
    {
        _dbContext.Set<PatchAuditRecord>().Add(new PatchAuditRecord
        {
            TimestampUtc = entry.TimestampUtc,
            UserName = entry.UserName,
            Action = entry.Action,
            ProductId = entry.ProductId,
            DepotId = entry.DepotId,
            TargetClientsJson = JsonSerializer.Serialize(entry.TargetClients),
            PreviewJson = entry.PreviewJson,
            Result = entry.Result,
            ErrorMessage = entry.ErrorMessage,
            OldVersion = entry.OldVersion,
            NewVersion = entry.NewVersion,
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PatchAuditEntry>> ListAsync(
        int limit, CancellationToken cancellationToken)
    {
        List<PatchAuditRecord> records = await _dbContext.Set<PatchAuditRecord>()
            .OrderByDescending(record => record.TimestampUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return [.. records.Select(record => new PatchAuditEntry(
            record.Id,
            record.TimestampUtc,
            record.UserName,
            record.Action,
            record.ProductId,
            record.DepotId,
            DeserializeTargets(record.TargetClientsJson),
            record.PreviewJson,
            record.Result,
            record.ErrorMessage,
            record.OldVersion,
            record.NewVersion))];
    }

    public async Task<PatchAuditEntry?> FindLatestAsync(
        string productId,
        string action,
        CancellationToken cancellationToken)
    {
        PatchAuditRecord? record = await _dbContext.Set<PatchAuditRecord>()
            .Where(entry => entry.ProductId == productId && entry.Action == action)
            .OrderByDescending(entry => entry.TimestampUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return record is null
            ? null
            : new PatchAuditEntry(
                record.Id,
                record.TimestampUtc,
                record.UserName,
                record.Action,
                record.ProductId,
                record.DepotId,
                DeserializeTargets(record.TargetClientsJson),
                record.PreviewJson,
                record.Result,
                record.ErrorMessage,
                record.OldVersion,
                record.NewVersion);
    }

    private static List<string> DeserializeTargets(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            // A corrupt audit row must never break the history view
            return [];
        }
    }
}
