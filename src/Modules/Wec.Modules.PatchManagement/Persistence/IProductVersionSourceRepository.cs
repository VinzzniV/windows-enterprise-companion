namespace Wec.Modules.PatchManagement.Persistence;

public sealed record ProductVersionSource(
    string ProductId,
    string SourceUrl,
    string VersionPattern,
    bool Enabled,
    string? LatestVersion,
    DateTimeOffset? LastCheckedUtc,
    string CheckStatus,
    string? LastError);

public interface IProductVersionSourceRepository
{
    Task<IReadOnlyList<ProductVersionSource>> ListAsync(CancellationToken cancellationToken);
    Task UpsertAsync(ProductVersionSource source, CancellationToken cancellationToken);
    Task DeleteAsync(string productId, CancellationToken cancellationToken);
}
