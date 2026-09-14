using Wec.Core.Microsoft365;
using Wec.Core.Results;

namespace Wec.Modules.Microsoft365.Domain;

public sealed record Microsoft365LicenseCapacity(string? SkuId, int? RemainingSeats,
    bool? NearlyExhausted, bool? OverAssigned);
public sealed record Microsoft365Snapshot(Microsoft365Query Query, Microsoft365Data? Data,
    DateTimeOffset? UpdatedAtUtc, bool Stale, Error? RefreshError,
    IReadOnlyList<Microsoft365LicenseCapacity> LicenseCapacity);
public sealed record Microsoft365SourceStatus(Microsoft365Resource Resource, DateTimeOffset? UpdatedAtUtc,
    bool Stale, bool Truncated, long? TotalCount, int? LoadedCount, Error? LastRefreshError);
public sealed record Microsoft365Status(Microsoft365Connection Connection,
    IReadOnlyList<Microsoft365SourceStatus> Sources);
public sealed record Microsoft365Correlation(string State, string Explanation,
    Microsoft365User? User, Microsoft365Device? Device, Microsoft365ManagedDevice? ManagedDevice,
    DateTimeOffset? ObservedAtUtc, bool Stale);
