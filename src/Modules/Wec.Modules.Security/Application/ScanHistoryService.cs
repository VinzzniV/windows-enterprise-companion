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

    public async Task<Result<ScanHistoryResult>> GetHistoryAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<SecurityScanResult> scans =
            await _repository.GetRecentScansAsync(_options.HistoryLimit, cancellationToken);

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
        ]);

    private static ScanDiff BuildDiff(SecurityScanResult latest, SecurityScanResult previous)
    {
        HashSet<(string, string)> previousKeys = [.. previous.Findings.Select(FindingIdentity)];
        HashSet<(string, string)> latestKeys = [.. latest.Findings.Select(FindingIdentity)];

        return new ScanDiff(
            latest.ScanId,
            previous.ScanId,
            [.. latest.Findings.Where(finding => !previousKeys.Contains(FindingIdentity(finding)))],
            [.. previous.Findings.Where(finding => !latestKeys.Contains(FindingIdentity(finding)))]);
    }

    private static (string, string) FindingIdentity(SecurityFinding finding) =>
        (finding.FindingId, finding.AffectedResource);
}
