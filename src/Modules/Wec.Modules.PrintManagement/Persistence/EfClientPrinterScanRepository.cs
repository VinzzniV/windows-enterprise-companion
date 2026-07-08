using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wec.Modules.PrintManagement.Domain;

namespace Wec.Modules.PrintManagement.Persistence;

/// <summary>Latest-per-host store for client printer scans (JSON blob, replace-on-save).</summary>
public sealed class EfClientPrinterScanRepository : IClientPrinterScanRepository
{
    private readonly DbContext _dbContext;
    private readonly ILogger<EfClientPrinterScanRepository> _logger;

    public EfClientPrinterScanRepository(DbContext dbContext, ILogger<EfClientPrinterScanRepository> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<ClientPrinterScan?> GetLatestAsync(string hostKey, CancellationToken cancellationToken)
    {
        ClientPrinterScanRecord? record = await _dbContext.Set<ClientPrinterScanRecord>()
            .Where(scan => scan.Host == hostKey)
            .OrderByDescending(scan => scan.CapturedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (record is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ClientPrinterScan>(record.PayloadJson);
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Discarding unreadable client printer scan {ScanId}", record.Id);
            return null;
        }
    }

    public async Task SaveAsync(ClientPrinterScan scan, CancellationToken cancellationToken)
    {
        // One entry per host; replace instead of accumulating history
        await _dbContext.Set<ClientPrinterScanRecord>()
            .Where(existing => existing.Host == scan.Host)
            .ExecuteDeleteAsync(cancellationToken);

        _dbContext.Set<ClientPrinterScanRecord>().Add(new ClientPrinterScanRecord
        {
            Host = scan.Host,
            CapturedAtUtc = scan.CapturedAtUtc,
            PayloadJson = JsonSerializer.Serialize(scan),
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
