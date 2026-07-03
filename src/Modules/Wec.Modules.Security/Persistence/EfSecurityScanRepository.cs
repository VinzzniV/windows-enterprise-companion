using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wec.Core.Privileges;
using Wec.Modules.Security.Domain;

namespace Wec.Modules.Security.Persistence;

/// <summary>
/// Works against the plain <see cref="DbContext"/> base type so the module never
/// references Infrastructure; the host wires the concrete context (dependency rule 2).
/// </summary>
public sealed class EfSecurityScanRepository : ISecurityScanRepository
{
    private readonly DbContext _dbContext;
    private readonly ILogger<EfSecurityScanRepository> _logger;

    public EfSecurityScanRepository(DbContext dbContext, ILogger<EfSecurityScanRepository> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<long> SaveScanAsync(
        string hostKey,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        ScanStatus status,
        IReadOnlyList<SecurityFinding> findings,
        CancellationToken cancellationToken)
    {
        var scanRecord = new SecurityScanRecord
        {
            Host = hostKey,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            Status = status.ToString(),
            FindingCount = findings.Count,
            Findings = findings.Select(ToFindingRecord).ToList(),
        };

        _dbContext.Set<SecurityScanRecord>().Add(scanRecord);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return scanRecord.Id;
    }

    public async Task<SecurityScanResult?> GetLatestScanAsync(string hostKey, CancellationToken cancellationToken)
    {
        SecurityScanRecord? scanRecord = await _dbContext.Set<SecurityScanRecord>()
            .Include(scan => scan.Findings)
            .Where(scan => scan.Host == hostKey)
            .OrderByDescending(scan => scan.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return scanRecord is null ? null : ToScanResult(scanRecord);
    }

    public async Task<IReadOnlyList<SecurityScanResult>> GetRecentScansAsync(
        string hostKey,
        int limit,
        CancellationToken cancellationToken)
    {
        List<SecurityScanRecord> scanRecords = await _dbContext.Set<SecurityScanRecord>()
            .Include(scan => scan.Findings)
            .Where(scan => scan.Host == hostKey)
            .OrderByDescending(scan => scan.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return [.. scanRecords.Select(ToScanResult)];
    }

    private SecurityScanResult ToScanResult(SecurityScanRecord record) => new(
        record.Id,
        record.Host,
        record.StartedAtUtc,
        record.CompletedAtUtc,
        Enum.TryParse(record.Status, out ScanStatus status) ? status : ScanStatus.Failed,
        record.Findings.Select(ToFinding).ToList());

    private SecurityFinding ToFinding(SecurityFindingRecord record) => new(
        record.FindingId,
        record.Title,
        record.Description,
        Enum.TryParse(record.Severity, out FindingSeverity severity) ? severity : FindingSeverity.Info,
        Enum.TryParse(record.Category, out FindingCategory category) ? category : FindingCategory.OperatingSystem,
        record.AffectedResource,
        DeserializeEvidence(record),
        record.Recommendation,
        Enum.TryParse(record.RequiredPrivilege, out PrivilegeLevel privilege) ? privilege : null,
        record.CapturedAtUtc);

    private Dictionary<string, string> DeserializeEvidence(SecurityFindingRecord record)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(record.EvidenceJson)
                ?? new Dictionary<string, string>();
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Discarding unreadable evidence of finding {FindingRecordId}", record.Id);
            return new Dictionary<string, string>();
        }
    }

    private static SecurityFindingRecord ToFindingRecord(SecurityFinding finding) => new()
    {
        FindingId = finding.FindingId,
        Title = finding.Title,
        Description = finding.Description,
        Severity = finding.Severity.ToString(),
        Category = finding.Category.ToString(),
        AffectedResource = finding.AffectedResource,
        EvidenceJson = JsonSerializer.Serialize(finding.Evidence),
        Recommendation = finding.Recommendation,
        RequiredPrivilege = finding.RequiredPrivilege?.ToString(),
        CapturedAtUtc = finding.CapturedAtUtc,
    };
}
