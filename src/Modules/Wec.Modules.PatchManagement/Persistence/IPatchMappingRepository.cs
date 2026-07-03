namespace Wec.Modules.PatchManagement.Persistence;

public sealed record ProductMapping(string SoftwareName, string OpsiProductId);

public interface IPatchMappingRepository
{
    Task<IReadOnlyList<ProductMapping>> ListAsync(CancellationToken cancellationToken);

    Task UpsertAsync(string softwareName, string opsiProductId, CancellationToken cancellationToken);

    Task DeleteAsync(string softwareName, CancellationToken cancellationToken);
}
