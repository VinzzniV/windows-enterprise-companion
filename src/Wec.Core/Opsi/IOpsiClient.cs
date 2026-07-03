using Wec.Core.Results;

namespace Wec.Core.Opsi;

/// <summary>
/// Read access to one opsi service plus the single gated write (rollout
/// action requests, ADR 0008). Stateless — every call carries the
/// connection. Implemented in Infrastructure over JSON-RPC.
/// </summary>
public interface IOpsiClient
{
    Task<Result<OpsiServerInfo>> TestConnectionAsync(
        OpsiConnection connection, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<OpsiDepot>>> GetDepotsAsync(
        OpsiConnection connection, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<OpsiClientHost>>> GetClientsAsync(
        OpsiConnection connection, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<OpsiProduct>>> GetProductsAsync(
        OpsiConnection connection, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<OpsiProductOnDepot>>> GetProductsOnDepotsAsync(
        OpsiConnection connection, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<OpsiProductOnClient>>> GetProductStatesAsync(
        OpsiConnection connection, CancellationToken cancellationToken);

    /// <summary>
    /// Sets <c>actionRequest = "setup"</c> on the given clients for one
    /// product — the only write, callers must have confirmed and audited it
    /// (ADR 0008). Returns the number of action requests written.
    /// </summary>
    Task<Result<int>> RequestSetupAsync(
        OpsiConnection connection,
        string productId,
        IReadOnlyList<string> clientIds,
        CancellationToken cancellationToken);
}
