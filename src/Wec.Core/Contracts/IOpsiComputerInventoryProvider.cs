using Wec.Core.Results;

namespace Wec.Core.Contracts;

/// <summary>Read-only opsi device data needed by the environment inventory.</summary>
public sealed record OpsiComputerInventoryItem(
    string ComputerName,
    string? Description,
    string? DepotId,
    DateTimeOffset? LastSeen,
    string? ClientAgentVersion);

public sealed record OpsiComputerInventory(
    IReadOnlyList<OpsiComputerInventoryItem> Computers,
    bool Truncated = false,
    string? SourceScope = null,
    Guid? SessionId = null);

/// <summary>
/// Cross-module read contract implemented by Patch Management. It uses the
/// existing process-scoped opsi session and exposes no write operation.
/// </summary>
public interface IOpsiComputerInventoryProvider
{
    Guid? CurrentSessionId { get; }
    Task<Result<OpsiComputerInventory>> LoadAsync(
        int limit,
        CancellationToken cancellationToken);
}
