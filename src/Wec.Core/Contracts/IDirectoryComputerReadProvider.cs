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

public interface IDirectoryComputerReadProvider
{
    Task<Result<DirectoryComputerIdentityResult>> ReadIdentityAsync(DirectoryComputerIdentityQuery query,
        CancellationToken cancellationToken);
}
