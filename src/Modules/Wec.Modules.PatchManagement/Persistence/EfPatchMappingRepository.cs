using Microsoft.EntityFrameworkCore;

namespace Wec.Modules.PatchManagement.Persistence;

/// <summary>
/// Works against the plain <see cref="DbContext"/> base type so the module never
/// references Infrastructure; the host wires the concrete context (dependency rule 2).
/// </summary>
public sealed class EfPatchMappingRepository : IPatchMappingRepository
{
    private readonly DbContext _dbContext;

    public EfPatchMappingRepository(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<ProductMapping>> ListAsync(CancellationToken cancellationToken) =>
        await _dbContext.Set<ProductMappingRecord>()
            .OrderBy(record => record.SoftwareName)
            .Select(record => new ProductMapping(record.SoftwareName, record.OpsiProductId))
            .ToListAsync(cancellationToken);

    public async Task UpsertAsync(
        string softwareName, string opsiProductId, CancellationToken cancellationToken)
    {
        ProductMappingRecord? existing = await _dbContext.Set<ProductMappingRecord>()
            .FirstOrDefaultAsync(record => record.SoftwareName == softwareName, cancellationToken);
        if (existing is null)
        {
            _dbContext.Set<ProductMappingRecord>().Add(new ProductMappingRecord
            {
                SoftwareName = softwareName,
                OpsiProductId = opsiProductId,
            });
        }
        else
        {
            existing.OpsiProductId = opsiProductId;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task DeleteAsync(string softwareName, CancellationToken cancellationToken) =>
        _dbContext.Set<ProductMappingRecord>()
            .Where(record => record.SoftwareName == softwareName)
            .ExecuteDeleteAsync(cancellationToken);
}
