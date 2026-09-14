using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Wec.Core.Contracts;
using Wec.Core.Results;

namespace Wec.Modules.Security.Persistence;

public sealed class SecurityStoredDeviceListProvider(DbContext dbContext) : IStoredDeviceListProvider
{
    public StoredDeviceListSource Source => StoredDeviceListSource.Security;
    public async Task<Result<StoredDeviceAddressPage>> ReadAsync(int maximumRecords, string? search, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRecords);
        try
        {
            await using var transaction = dbContext.Database.CurrentTransaction is null
                ? await dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
            IQueryable<SecurityScanRecord> stored = dbContext.Set<SecurityScanRecord>().AsNoTracking()
                .Where(record => record.Host.Trim() != string.Empty);
            IQueryable<SecurityScanRecord> selected = stored.Where(record => !stored.Any(other => other.Host == record.Host && other.Id > record.Id));
            if (!string.IsNullOrWhiteSpace(search))
            {
                string pattern = "%" + search.Trim().Replace("!", "!!", StringComparison.Ordinal)
                    .Replace("%", "!%", StringComparison.Ordinal).Replace("_", "!_", StringComparison.Ordinal) + "%";
                selected = selected.Where(record => EF.Functions.Like(record.Host, pattern, "!"));
            }
            int total = await selected.CountAsync(cancellationToken);
            var records = await selected.OrderBy(record => record.Host).ThenBy(record => record.Id).Take(maximumRecords)
                .Select(record => new { record.Id, record.Host, Label = record.Host, ObservedAtUtc = record.CompletedAtUtc })
                .ToListAsync(cancellationToken);
            return Result.Success(new StoredDeviceAddressPage(total, records.Select(record => new StoredDeviceAddressRow(
                record.Id.ToString(CultureInfo.InvariantCulture), record.Host, record.Label, record.ObservedAtUtc)).ToArray()));
        }
        catch (DbException)
        {
            return Result.Failure<StoredDeviceAddressPage>(new(ErrorCode.ServiceUnavailable, "The stored Security address list could not be read."));
        }
    }
}
