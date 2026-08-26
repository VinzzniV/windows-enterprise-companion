using Wec.Core.Results;

namespace Wec.Core.Opsi;

/// <summary>
/// Read access to one opsi service. Client action requests are deliberately
/// outside WEC; every call carries the connection.
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

    /// <summary>Reads only one product's client states to keep inventory payloads small.</summary>
    Task<Result<IReadOnlyList<OpsiProductOnClient>>> GetProductStatesAsync(
        OpsiConnection connection,
        string productId,
        CancellationToken cancellationToken);

}
