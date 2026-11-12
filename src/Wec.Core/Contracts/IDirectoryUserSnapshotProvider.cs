using Wec.Core.Results;

namespace Wec.Core.Contracts;

public sealed record DirectoryUserLookup(
    DirectoryUserReadConnection Connection,
    string DirectoryScope,
    Guid? ObjectId = null,
    string? SecurityIdentifier = null);

public sealed record DirectoryUserIdentityResult(
    string DirectoryScope,
    DateTimeOffset RetrievedAtUtc,
    DirectoryUserRecord? User);

public sealed record CachedDirectoryUser(
    DirectoryUserIdentityResult? Data,
    DateTimeOffset LastAttemptAtUtc,
    Error? LastAttemptError,
    long SessionRevision,
    long Revision,
    DateTimeOffset? RetainedUntilUtc,
    bool Stale,
    DateTimeOffset? FreshUntilUtc);

public interface IDirectoryUserSnapshotProvider
{
    Task<CachedDirectoryUser?> ReadCachedAsync(DirectoryUserLookup query, CancellationToken cancellationToken);
    Task<Result<DirectoryUserIdentityResult>> ReadIdentityAsync(DirectoryUserLookup query, CancellationToken cancellationToken);
}
