using Microsoft.EntityFrameworkCore;

namespace Wec.Modules.PatchManagement.Persistence;

public sealed class EfWingetManagedPackageRepository : IWingetManagedPackageRepository
{
    private readonly DbContext _dbContext;

    public EfWingetManagedPackageRepository(DbContext dbContext) => _dbContext = dbContext;

    public async Task<IReadOnlyList<WingetManagedPackage>> ListAsync(CancellationToken cancellationToken) =>
        [.. (await _dbContext.Set<WingetManagedPackageRecord>()
            .AsNoTracking()
            .OrderBy(record => record.DisplayName)
            .ThenBy(record => record.OpsiProductId)
            .ToListAsync(cancellationToken))
            .Select(Map)];

    public async Task<WingetManagedPackage?> FindByProductIdAsync(
        string productId,
        CancellationToken cancellationToken)
    {
        WingetManagedPackageRecord? record = await _dbContext.Set<WingetManagedPackageRecord>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.OpsiProductId == productId,
                cancellationToken);
        return record is null ? null : Map(record);
    }

    public async Task UpsertAsync(WingetManagedPackage package, CancellationToken cancellationToken)
    {
        WingetManagedPackageRecord? record = await _dbContext.Set<WingetManagedPackageRecord>()
            .FirstOrDefaultAsync(
                item => item.OpsiProductId == package.OpsiProductId,
                cancellationToken);
        if (record is null)
        {
            record = new WingetManagedPackageRecord();
            _dbContext.Set<WingetManagedPackageRecord>().Add(record);
        }

        record.OpsiProductId = package.OpsiProductId;
        record.WingetId = package.WingetId;
        record.Source = package.Source;
        record.Scope = package.Scope;
        record.DepotId = package.DepotId;
        record.DisplayName = package.DisplayName;
        record.LastPackagedWingetVersion = package.LastPackagedWingetVersion;
        record.TemplateVersion = package.TemplateVersion;
        record.LatestWingetVersion = package.LatestWingetVersion;
        record.CheckStatus = package.CheckStatus;
        record.CheckedAtUtc = package.CheckedAtUtc;
        record.LastError = package.LastError;
        record.CreatedAtUtc = package.CreatedAtUtc;
        record.UpdatedAtUtc = package.UpdatedAtUtc;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static WingetManagedPackage Map(WingetManagedPackageRecord record) => new(
        record.Id,
        record.OpsiProductId,
        record.WingetId,
        record.Source,
        record.Scope,
        record.DepotId,
        record.DisplayName,
        record.LastPackagedWingetVersion,
        record.TemplateVersion,
        record.LatestWingetVersion,
        record.CheckStatus,
        record.CheckedAtUtc,
        record.LastError,
        record.CreatedAtUtc,
        record.UpdatedAtUtc);
}
