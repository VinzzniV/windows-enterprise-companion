using Wec.Modules.Security.Domain;

namespace Wec.Modules.Security.Persistence;

public interface ISecurityScanRepository
{
    Task<long> SaveScanAsync(
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        ScanStatus status,
        IReadOnlyList<SecurityFinding> findings,
        CancellationToken cancellationToken);

    Task<SecurityScanResult?> GetLatestScanAsync(CancellationToken cancellationToken);
}
