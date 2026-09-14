using Wec.Core.Results;

namespace Wec.Core.Microsoft365;

public enum Microsoft365Availability { NotCached, Available, Unavailable, NotEnabled, NotConnected }
public enum EvidenceFreshness { Unknown, Fresh, Stale }
public enum EvidenceCoverage { Unknown, ReturnedSet, Partial }

public sealed record Microsoft365ReadState(
    Microsoft365Query Query,
    string? TenantId,
    long SessionRevision,
    long SnapshotRevision,
    Microsoft365Availability Availability,
    bool Loading,
    DateTimeOffset? RetrievedAtUtc,
    DateTimeOffset? LastAttemptAtUtc,
    Error? LastAttemptError,
    DateTimeOffset? RetainedUntilUtc,
    EvidenceFreshness Freshness,
    EvidenceCoverage Coverage,
    int? LoadedCount,
    long? DeclaredTotal);
