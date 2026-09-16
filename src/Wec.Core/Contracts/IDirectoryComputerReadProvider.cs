using Wec.Core.Results;

namespace Wec.Core.Contracts;

public sealed record DirectoryComputerIdentityQuery(
    DirectoryUserReadConnection Connection,
    string DirectoryScope,
    Guid? ObjectId = null,
    string? SecurityIdentifier = null);

public sealed record DirectoryComputerIdentityResult(
    string DirectoryScope,
    DateTimeOffset RetrievedAtUtc,
    IReadOnlyList<AdComputerInventoryItem> Computers,
    bool Truncated);

public sealed record CachedDirectoryComputer(
    DirectoryComputerIdentityResult? Data,
    DateTimeOffset LastAttemptAtUtc,
    Error? LastAttemptError,
    long SessionRevision,
    long Revision,
    DateTimeOffset? RetainedUntilUtc,
    bool Stale);

public interface IDirectoryComputerReadProvider
{
    Task<CachedDirectoryComputer?> ReadCachedAsync(DirectoryComputerIdentityQuery query, CancellationToken cancellationToken);

    Task<Result<DirectoryComputerIdentityResult>> ReadIdentityAsync(DirectoryComputerIdentityQuery query,
        CancellationToken cancellationToken);
}
