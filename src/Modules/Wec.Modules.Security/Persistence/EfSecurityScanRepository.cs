using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wec.Core.Privileges;
using Wec.Core.Results;
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
            Findings = findings.Select(finding => ToFindingRecord(finding)).ToList(),
        };

        return await SaveRecordAsync(scanRecord, cancellationToken);
    }

    public async Task<long> SaveScanAsync(
        string hostKey,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        ScanStatus status,
        int coverageVersion,
        IReadOnlyList<SecurityCheckResult> checkResults,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(coverageVersion);

        string? duplicateCheckId = checkResults
            .GroupBy(result => result.CheckId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
        if (duplicateCheckId is not null)
        {
            throw new ArgumentException(
                $"Security check '{duplicateCheckId}' produced more than one execution result.",
                nameof(checkResults));
        }

        var scanRecord = new SecurityScanRecord
        {
            Host = hostKey,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            Status = status.ToString(),
            CoverageVersion = coverageVersion,
            FindingCount = checkResults.Sum(result => result.Findings.Count),
            CheckResults = checkResults.Select(ToCheckResultRecord).ToList(),
            Findings = checkResults
                .SelectMany(result => result.Findings.Select(finding => ToFindingRecord(finding, result.CheckId)))
                .ToList(),
        };

        return await SaveRecordAsync(scanRecord, cancellationToken);
    }

    private async Task<long> SaveRecordAsync(
        SecurityScanRecord scanRecord,
        CancellationToken cancellationToken)
    {
        _dbContext.Set<SecurityScanRecord>().Add(scanRecord);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return scanRecord.Id;
    }

    public async Task<SecurityScanResult?> GetLatestScanAsync(string hostKey, CancellationToken cancellationToken)
    {
        SecurityScanRecord? scanRecord = await _dbContext.Set<SecurityScanRecord>()
            .Include(scan => scan.Findings)
            .Include(scan => scan.CheckResults)
            .Where(scan => scan.Host == hostKey)
            .OrderByDescending(scan => scan.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return scanRecord is null ? null : ToScanResult(scanRecord);
    }

    public async Task<IReadOnlyList<StoredSecurityScanHost>> ListHostsAsync(
        CancellationToken cancellationToken) =>
        await _dbContext.Set<SecurityScanRecord>()
            .Where(scan => !_dbContext.Set<SecurityScanRecord>().Any(newer =>
                newer.Host == scan.Host && newer.Id > scan.Id))
            .OrderBy(scan => scan.Host)
            .Select(scan => new StoredSecurityScanHost(scan.Host, scan.CompletedAtUtc))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SecurityScanResult>> ListLatestScansAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        List<SecurityScanRecord> records = await _dbContext.Set<SecurityScanRecord>()
            .Where(scan => !_dbContext.Set<SecurityScanRecord>().Any(newer =>
                newer.Host == scan.Host && newer.Id > scan.Id))
            .OrderBy(scan => scan.Host)
            .Take(limit)
            .Include(scan => scan.Findings)
            .Include(scan => scan.CheckResults)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        return [.. records.Select(ToScanResult)];
    }

    public async Task<IReadOnlyList<SecurityScanResult>> GetRecentScansAsync(
        string hostKey,
        int limit,
        CancellationToken cancellationToken)
    {
        List<SecurityScanRecord> scanRecords = await _dbContext.Set<SecurityScanRecord>()
            .Include(scan => scan.Findings)
            .Include(scan => scan.CheckResults)
            .Where(scan => scan.Host == hostKey)
            .OrderByDescending(scan => scan.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return [.. scanRecords.Select(ToScanResult)];
    }

    private SecurityScanResult ToScanResult(SecurityScanRecord record)
    {
        List<SecurityFinding> findings = record.Findings.Select(ToFinding).ToList();
        Dictionary<string, IReadOnlyList<SecurityFinding>> findingsByCheckId = record.Findings
            .Where(finding => finding.CheckId is not null)
            .GroupBy(finding => finding.CheckId!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<SecurityFinding>)[.. group.Select(ToFinding)],
                StringComparer.Ordinal);

        var checkResults = new List<SecurityCheckResult>(record.CheckResults.Count);
        var invalidCheckResult = false;
        foreach (SecurityCheckResultRecord checkResultRecord in record.CheckResults.OrderBy(result => result.CheckId))
        {
            findingsByCheckId.TryGetValue(checkResultRecord.CheckId, out IReadOnlyList<SecurityFinding>? checkFindings);
            checkResults.Add(ToCheckResult(checkResultRecord, checkFindings ?? [], out bool isValid));
            invalidCheckResult |= !isValid;
        }

        HashSet<string> persistedCheckIds = [.. record.CheckResults.Select(result => result.CheckId)];
        bool hasOrphanFinding = record.Findings.Any(finding =>
            finding.CheckId is null || !persistedCheckIds.Contains(finding.CheckId));
        int? coverageVersion = invalidCheckResult || hasOrphanFinding
            ? null
            : record.CoverageVersion;

        if (record.CoverageVersion is not null && coverageVersion is null)
        {
            _logger.LogWarning(
                "Treating security scan {ScanId} as coverage-unknown because its persisted check data is inconsistent",
                record.Id);
        }

        return new SecurityScanResult(
            record.Id,
            record.Host,
            record.StartedAtUtc,
            record.CompletedAtUtc,
            Enum.TryParse(record.Status, out ScanStatus status) ? status : ScanStatus.Failed,
            findings,
            checkResults,
            coverageVersion);
    }

    private SecurityCheckResult ToCheckResult(
        SecurityCheckResultRecord record,
        IReadOnlyList<SecurityFinding> findings,
        out bool isValid)
    {
        isValid = Enum.TryParse(record.Status, out CheckStatus status)
            && Enum.IsDefined(status)
            && string.Equals(record.Status, status.ToString(), StringComparison.Ordinal);
        if (!isValid)
        {
            status = CheckStatus.Failed;
            _logger.LogWarning(
                "Security check result {CheckResultRecordId} has unknown status {Status}",
                record.Id,
                record.Status);
        }

        SecurityCheckFailure? failure = ToFailure(record, ref isValid);
        if (!isValid && failure is null)
        {
            failure = new SecurityCheckFailure(
                ErrorCode.InternalError,
                "The stored check execution status could not be read.",
                RequiredPrivilege: null);
        }

        if (status == CheckStatus.Succeeded && failure is not null)
        {
            isValid = false;
            status = CheckStatus.Failed;
            failure = new SecurityCheckFailure(
                ErrorCode.InternalError,
                "The stored check execution result is inconsistent.",
                RequiredPrivilege: null);
        }

        return new SecurityCheckResult(record.CheckId, status, findings, failure);
    }

    private static SecurityCheckFailure? ToFailure(SecurityCheckResultRecord record, ref bool isValid)
    {
        if (record.FailureCode is null && record.FailureMessage is null && record.RequiredPrivilege is null)
        {
            return null;
        }

        if (!Enum.TryParse(record.FailureCode, out ErrorCode errorCode)
            || !Enum.IsDefined(errorCode)
            || !string.Equals(record.FailureCode, errorCode.ToString(), StringComparison.Ordinal))
        {
            isValid = false;
            return new SecurityCheckFailure(
                ErrorCode.InternalError,
                "The stored check failure could not be read.",
                RequiredPrivilege: null);
        }

        PrivilegeLevel? requiredPrivilege = null;
        if (record.RequiredPrivilege is not null)
        {
            if (Enum.TryParse(record.RequiredPrivilege, out PrivilegeLevel parsedPrivilege)
                && Enum.IsDefined(parsedPrivilege)
                && string.Equals(
                    record.RequiredPrivilege,
                    parsedPrivilege.ToString(),
                    StringComparison.Ordinal))
            {
                requiredPrivilege = parsedPrivilege;
            }
            else
            {
                isValid = false;
            }
        }

        return new SecurityCheckFailure(
            errorCode,
            record.FailureMessage ?? "The check did not complete.",
            requiredPrivilege);
    }

    private SecurityFinding ToFinding(SecurityFindingRecord record) => new(
        record.FindingId,
        record.Title,
        record.Description,
        Enum.TryParse(record.Severity, out FindingSeverity severity) && Enum.IsDefined(severity)
            ? severity
            : FindingSeverity.Unknown,
        Enum.TryParse(record.Category, out FindingCategory category) && Enum.IsDefined(category)
            ? category
            : FindingCategory.Unknown,
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

    private static SecurityFindingRecord ToFindingRecord(SecurityFinding finding, string? checkId = null) => new()
    {
        CheckId = checkId,
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

    private static SecurityCheckResultRecord ToCheckResultRecord(SecurityCheckResult result) => new()
    {
        CheckId = result.CheckId,
        Status = result.Status.ToString(),
        FailureCode = result.Failure?.Code.ToString(),
        FailureMessage = result.Failure?.Message,
        RequiredPrivilege = result.Failure?.RequiredPrivilege?.ToString(),
    };
}
