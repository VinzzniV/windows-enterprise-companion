namespace Wec.Modules.PatchManagement.Persistence;

public sealed record WingetManagedPackage(
    int Id,
    string OpsiProductId,
    string WingetId,
    string Source,
    string Scope,
    string DepotId,
    string DisplayName,
    string? LastPackagedWingetVersion,
    int TemplateVersion,
    string? LatestWingetVersion,
    string CheckStatus,
    DateTimeOffset? CheckedAtUtc,
    string? LastError,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public interface IWingetManagedPackageRepository
{
    Task<IReadOnlyList<WingetManagedPackage>> ListAsync(CancellationToken cancellationToken);
    Task<WingetManagedPackage?> FindByProductIdAsync(string productId, CancellationToken cancellationToken);
    Task UpsertAsync(WingetManagedPackage package, CancellationToken cancellationToken);
}
