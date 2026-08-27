using Wec.Modules.Security.Domain;

namespace Wec.Modules.Security.Persistence;

public sealed record StoredSecurityScanHost(string Host, DateTimeOffset CompletedAtUtc);

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

    /// <summary>One latest persisted scan stamp per host, without findings or check details.</summary>
    Task<IReadOnlyList<StoredSecurityScanHost>> ListHostsAsync(CancellationToken cancellationToken);

    /// <summary>Latest persisted scans across hosts, loaded in one bounded query.</summary>
    Task<IReadOnlyList<SecurityScanResult>> ListLatestScansAsync(
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Most recent scans of one host first, including findings.</summary>
    Task<IReadOnlyList<SecurityScanResult>> GetRecentScansAsync(
        string hostKey,
        int limit,
        CancellationToken cancellationToken);
}
