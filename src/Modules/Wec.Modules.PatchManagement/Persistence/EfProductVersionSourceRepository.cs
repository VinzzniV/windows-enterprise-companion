using Microsoft.EntityFrameworkCore;

namespace Wec.Modules.PatchManagement.Persistence;

public sealed class EfProductVersionSourceRepository : IProductVersionSourceRepository
{
    private readonly DbContext _dbContext;

    public EfProductVersionSourceRepository(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<ProductVersionSource>> ListAsync(CancellationToken cancellationToken) =>
        await _dbContext.Set<ProductVersionSourceRecord>()
            .OrderBy(record => record.ProductId)
            .Select(record => new ProductVersionSource(
                record.ProductId,
                record.SourceUrl,
                record.VersionPattern,
                record.Enabled,
                record.LatestVersion,
                record.LastCheckedUtc,
                record.CheckStatus,
                record.LastError))
            .ToListAsync(cancellationToken);

    public async Task UpsertAsync(ProductVersionSource source, CancellationToken cancellationToken)
    {
        ProductVersionSourceRecord? record = await _dbContext.Set<ProductVersionSourceRecord>()
            .FirstOrDefaultAsync(item => item.ProductId == source.ProductId, cancellationToken);
        if (record is null)
        {
            record = new ProductVersionSourceRecord { ProductId = source.ProductId };
            _dbContext.Set<ProductVersionSourceRecord>().Add(record);
        }

        record.SourceUrl = source.SourceUrl;
        record.VersionPattern = source.VersionPattern;
        record.Enabled = source.Enabled;
        record.LatestVersion = source.LatestVersion;
        record.LastCheckedUtc = source.LastCheckedUtc;
        record.CheckStatus = source.CheckStatus;
        record.LastError = source.LastError;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task DeleteAsync(string productId, CancellationToken cancellationToken) =>
        _dbContext.Set<ProductVersionSourceRecord>()
            .Where(record => record.ProductId == productId)
            .ExecuteDeleteAsync(cancellationToken);
}
