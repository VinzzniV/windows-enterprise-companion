using Microsoft.Extensions.Options;
using Wec.Core.Results;
using Wec.Modules.Security.Domain;
using Wec.Modules.Security.Persistence;

namespace Wec.Modules.Security.Application;

internal sealed class ScanHistoryService
{
    private readonly ISecurityScanRepository _repository;
    private readonly SecurityOptions _options;

    // DI requires a public constructor even on internal types
    public ScanHistoryService(ISecurityScanRepository repository, IOptions<SecurityOptions> options)
    {
        _repository = repository;
        _options = options.Value;
    }

    public async Task<Result<ScanHistoryResult>> GetHistoryAsync(
        Wec.Core.Targets.ScanTarget target,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SecurityScanResult> scans =
            await _repository.GetRecentScansAsync(target.CacheKey, _options.HistoryLimit, cancellationToken);

        ScanDiff? changes = scans.Count >= 2 ? BuildDiff(scans[0], scans[1]) : null;
        return Result.Success(new ScanHistoryResult([.. scans.Select(ToSummary)], changes));
    }

    private static ScanSummary ToSummary(SecurityScanResult scan) => new(
        scan.ScanId,
        scan.StartedAtUtc,
        scan.CompletedAtUtc,
        scan.Status,
        scan.Findings.Count,
        [
            .. scan.Findings
                .GroupBy(finding => finding.Severity)
                .OrderByDescending(group => group.Key)
                .Select(group => new SeverityCount(group.Key, group.Count())),
        ],
        scan.Coverage);

    private static ScanDiff BuildDiff(SecurityScanResult latest, SecurityScanResult previous)
    {
        if (!latest.Coverage.IsKnown || !previous.Coverage.IsKnown)
        {
            return new ScanDiff(
                latest.ScanId,
                previous.ScanId,
                [],
                [],
                IsFullyComparable: false,
                UncomparedCheckIds: []);
        }

        Dictionary<string, SecurityCheckResult> latestChecks = latest.CheckResults
            .ToDictionary(result => result.CheckId, StringComparer.Ordinal);
        Dictionary<string, SecurityCheckResult> previousChecks = previous.CheckResults
            .ToDictionary(result => result.CheckId, StringComparer.Ordinal);
        List<string> checkIds = latestChecks.Keys
            .Union(previousChecks.Keys, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        var newFindings = new List<SecurityFinding>();
        var resolvedFindings = new List<SecurityFinding>();
        var uncomparedCheckIds = new List<string>();

        foreach (string checkId in checkIds)
        {
            if (!latestChecks.TryGetValue(checkId, out SecurityCheckResult? latestCheck)
                || latestCheck.Status != CheckStatus.Succeeded
                || !previousChecks.TryGetValue(checkId, out SecurityCheckResult? previousCheck)
                || previousCheck.Status != CheckStatus.Succeeded)
            {
                uncomparedCheckIds.Add(checkId);
                continue;
            }

            HashSet<(string, string)> previousKeys = [.. previousCheck.Findings.Select(FindingIdentity)];
            HashSet<(string, string)> latestKeys = [.. latestCheck.Findings.Select(FindingIdentity)];

            newFindings.AddRange(
                latestCheck.Findings.Where(finding => !previousKeys.Contains(FindingIdentity(finding))));
            resolvedFindings.AddRange(
                previousCheck.Findings.Where(finding => !latestKeys.Contains(FindingIdentity(finding))));
        }

        return new ScanDiff(
            latest.ScanId,
            previous.ScanId,
            newFindings,
            resolvedFindings,
            IsFullyComparable: checkIds.Count > 0 && uncomparedCheckIds.Count == 0,
            uncomparedCheckIds);
    }

    private static (string, string) FindingIdentity(SecurityFinding finding) =>
        (finding.FindingId, finding.AffectedResource);
}
