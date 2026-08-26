using Wec.Core.Results;

namespace Wec.Core.SoftwareUpdates;

public sealed record WingetPackageInfo(
    string Id,
    string Name,
    string Publisher,
    string Version,
    string Source,
    string InstallerType,
    string Architecture,
    string Scope,
    bool IsEligible,
    string? IneligibilityReason);

/// <summary>
/// Read-only access to the public Windows Package Manager catalog. Implementations
/// must use the typed deployment API and must not parse localized winget output.
/// </summary>
public interface IWingetCatalogClient
{
    Task<Result<IReadOnlyList<WingetPackageInfo>>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken);

    Task<Result<WingetPackageInfo>> GetExactAsync(
        string packageId,
        CancellationToken cancellationToken);

    Task<Result<int>> CompareVersionsAsync(
        string packageId,
        string leftVersion,
        string rightVersion,
        CancellationToken cancellationToken);
}
