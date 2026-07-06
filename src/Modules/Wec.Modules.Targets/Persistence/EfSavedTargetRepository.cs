using Microsoft.EntityFrameworkCore;

namespace Wec.Modules.Targets.Persistence;

/// <summary>
/// Works against the plain <see cref="DbContext"/> base type so the module never
/// references Infrastructure; the host wires the concrete context (dependency rule 2).
/// The table holds a handful of servers, so upsert matching happens in memory
/// (avoids relying on SQLite case-insensitive collation for the host/role match).
/// </summary>
public sealed class EfSavedTargetRepository : ISavedTargetRepository
{
    private readonly DbContext _dbContext;

    public EfSavedTargetRepository(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<SavedTarget>> ListAsync(CancellationToken cancellationToken) =>
        (await _dbContext.Set<SavedTargetRecord>()
            .OrderBy(record => record.Role)
            .ThenBy(record => record.Label)
            .ToListAsync(cancellationToken))
            .Select(ToDto)
            .ToList();

    public async Task<SavedTarget> UpsertAsync(
        string label, string host, string role, string? userName,
        DateTimeOffset createdAtUtc, CancellationToken cancellationToken)
    {
        List<SavedTargetRecord> all =
            await _dbContext.Set<SavedTargetRecord>().ToListAsync(cancellationToken);
        SavedTargetRecord? existing = all.FirstOrDefault(record =>
            string.Equals(record.Host, host, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(record.Role, role, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            existing = new SavedTargetRecord
            {
                Host = host,
                Role = role,
                CreatedAtUtc = createdAtUtc,
            };
            _dbContext.Set<SavedTargetRecord>().Add(existing);
        }

        existing.Label = label;
        existing.UserName = userName;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(existing);
    }

    public Task DeleteAsync(int id, CancellationToken cancellationToken) =>
        _dbContext.Set<SavedTargetRecord>()
            .Where(record => record.Id == id)
            .ExecuteDeleteAsync(cancellationToken);

    private static SavedTarget ToDto(SavedTargetRecord record) => new(
        record.Id, record.Label, record.Host, record.Role, record.UserName, record.CreatedAtUtc);
}
