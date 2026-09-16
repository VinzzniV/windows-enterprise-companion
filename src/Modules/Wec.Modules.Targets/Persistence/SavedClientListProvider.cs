using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Wec.Core.Contracts;
using Wec.Core.Results;

namespace Wec.Modules.Targets.Persistence;

public sealed class SavedClientListProvider(DbContext dbContext) : IStoredDeviceListProvider
{
    public StoredDeviceListSource Source => StoredDeviceListSource.SavedClients;
    public async Task<Result<StoredDeviceAddressPage>> ReadAsync(int maximumRecords, string? search, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRecords);
        try
        {
            await using var transaction = dbContext.Database.CurrentTransaction is null
                ? await dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
            IQueryable<SavedTargetRecord> stored = dbContext.Set<SavedTargetRecord>().AsNoTracking()
                .Where(record => record.Host.Trim() != string.Empty);
            IQueryable<SavedTargetRecord> selected = stored.Where(record => record.Role == TargetRoles.Client);
            if (!string.IsNullOrWhiteSpace(search))
            {
                string pattern = "%" + search.Trim().Replace("!", "!!", StringComparison.Ordinal)
                    .Replace("%", "!%", StringComparison.Ordinal).Replace("_", "!_", StringComparison.Ordinal) + "%";
                selected = selected.Where(record => EF.Functions.Like(record.Host, pattern, "!") || EF.Functions.Like(record.Label, pattern, "!"));
            }
            int total = await selected.CountAsync(cancellationToken);
            string? exact = search?.Trim();
            var records = await selected.OrderBy(record => exact != null && EF.Functions.Collate(record.Host, "NOCASE") == exact ? 0 : 1)
                .ThenBy(record => record.Host).ThenBy(record => record.Id).Take(maximumRecords)
                .Select(record => new { record.Id, record.Host, Label = record.Label, ObservedAtUtc = record.CreatedAtUtc })
                .ToListAsync(cancellationToken);
            return Result.Success(new StoredDeviceAddressPage(total, records.Select(record => new StoredDeviceAddressRow(
                record.Id.ToString(CultureInfo.InvariantCulture), record.Host, record.Label, record.ObservedAtUtc)).ToArray()));
        }
        catch (DbException)
        {
            return Result.Failure<StoredDeviceAddressPage>(new(ErrorCode.ServiceUnavailable, "The stored SavedClients address list could not be read."));
        }
    }
}
