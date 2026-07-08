using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wec.Modules.Diagnostics.Domain;

namespace Wec.Modules.Diagnostics.Persistence;

/// <summary>Latest-per-host store for diagnostic runs (JSON blob, replace-on-save).</summary>
public sealed class EfDiagnosticRunRepository : IDiagnosticRunRepository
{
    private readonly DbContext _dbContext;
    private readonly ILogger<EfDiagnosticRunRepository> _logger;

    public EfDiagnosticRunRepository(DbContext dbContext, ILogger<EfDiagnosticRunRepository> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<DiagnosticRunResult?> GetLatestAsync(string hostKey, CancellationToken cancellationToken)
    {
        DiagnosticRunRecord? record = await _dbContext.Set<DiagnosticRunRecord>()
            .Where(run => run.Host == hostKey)
            .OrderByDescending(run => run.CompletedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (record is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<DiagnosticRunResult>(record.PayloadJson);
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Discarding unreadable diagnostic run {RunId}", record.Id);
            return null;
        }
    }

    public async Task SaveAsync(string hostKey, DiagnosticRunResult run, CancellationToken cancellationToken)
    {
        // One entry per host; replace instead of accumulating history
        await _dbContext.Set<DiagnosticRunRecord>()
            .Where(existing => existing.Host == hostKey)
            .ExecuteDeleteAsync(cancellationToken);

        _dbContext.Set<DiagnosticRunRecord>().Add(new DiagnosticRunRecord
        {
            Host = hostKey,
            CompletedAtUtc = run.CompletedAtUtc,
            PayloadJson = JsonSerializer.Serialize(run),
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
