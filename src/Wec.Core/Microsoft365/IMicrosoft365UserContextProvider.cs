using Wec.Core.Results;

namespace Wec.Core.Microsoft365;

public sealed record CachedEntraUsers(Microsoft365ReadState State, IReadOnlyList<Microsoft365User> Users);
public sealed record CachedMicrosoft365Groups(Microsoft365ReadState State, IReadOnlyList<Microsoft365Group> Groups);
public sealed record CachedMicrosoft365Licenses(Microsoft365ReadState State, IReadOnlyList<Microsoft365License> Licenses);
public sealed record CachedMicrosoft365Activity(Microsoft365ReadState State, Microsoft365Activity? Activity);

public sealed record Microsoft365UserContext(
    string? TenantId,
    long SessionRevision,
    long Revision,
    IReadOnlyList<CachedEntraUsers> UserReads,
    CachedMicrosoft365Licenses TenantLicenses,
    CachedMicrosoft365Licenses? UserLicenses,
    CachedMicrosoft365Groups? DirectGroups,
    CachedEntraDevices? RegisteredDevices,
    IReadOnlyList<CachedIntuneDevices> AssociatedIntune,
    CachedMicrosoft365Activity? SignIn,
    CachedMicrosoft365Activity? Registration);

public interface IMicrosoft365UserContextProvider
{
    Task<Result<Microsoft365UserContext>> ReadCachedAsync(string? tenantId, string? userObjectId,
        string? securityIdentifier, CancellationToken cancellationToken);
}
