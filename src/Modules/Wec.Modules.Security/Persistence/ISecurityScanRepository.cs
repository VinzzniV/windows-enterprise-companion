using Wec.Modules.Security.Domain;

namespace Wec.Modules.Security.Persistence;

public interface ISecurityScanRepository
{
    Task<long> SaveScanAsync(
        string hostKey,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        ScanStatus status,
        IReadOnlyList<SecurityFinding> findings,
        CancellationToken cancellationToken);

    Task<long> SaveScanAsync(
        string hostKey,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        ScanStatus status,
        int coverageVersion,
        IReadOnlyList<SecurityCheckResult> checkResults,
        CancellationToken cancellationToken);

    Task<SecurityScanResult?> GetLatestScanAsync(string hostKey, CancellationToken cancellationToken);

    /// <summary>Most recent scans of one host first, including findings.</summary>
    Task<IReadOnlyList<SecurityScanResult>> GetRecentScansAsync(
        string hostKey,
        int limit,
        CancellationToken cancellationToken);
}
