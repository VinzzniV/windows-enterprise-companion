using Wec.Core.Results;

namespace Wec.Core.Contracts;

public enum NessusInventoryAvailability
{
    Available = 0,
    Partial,
    NotConnected,
    Unavailable,
}

public sealed record NessusComputerInventoryItem(
    string ComputerName,
    string? AssetId,
    string? IpAddress,
    DateTimeOffset? LastCompletedScanUtc,
    int Critical,
    int High,
    int Medium,
    int Low,
    int Info,
    IReadOnlyList<int> Ports,
    IReadOnlyList<string> ScanSources,
    string? SourceKey = null,
    string? Fqdn = null,
    string? HostUuid = null,
    string? BiosUuid = null);

public sealed record NessusComputerInventory(
    IReadOnlyList<NessusComputerInventoryItem> Computers,
    NessusInventoryAvailability Availability,
    DateTimeOffset? LastSuccessfulSyncUtc,
    string? Error = null,
    int StaleWarningDays = 14,
    int StaleCriticalDays = 30,
    IReadOnlyList<string>? MissingExcludedOuPatterns = null,
    IReadOnlyList<string>? MissingExcludedHostPatterns = null);

/// <summary>Read-only cached Nessus inventory used by Environment Health.</summary>
public interface INessusComputerInventoryProvider
{
    Task<Result<NessusComputerInventory>> LoadAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Reads the latest persisted Nessus projection without starting a foreground
/// or background synchronization.
/// </summary>
public interface IStoredNessusComputerInventoryProvider
{
    Task<Result<NessusComputerInventory>> LoadStoredAsync(CancellationToken cancellationToken);
}
