using Wec.Core.Results;

namespace Wec.Core.Microsoft365;

public sealed record CachedEntraDevices(Microsoft365ReadState State, IReadOnlyList<Microsoft365Device> Devices);
public sealed record CachedIntuneDevices(Microsoft365ReadState State, IReadOnlyList<Microsoft365ManagedDevice> Devices);
public sealed record CachedMicrosoft365Members(Microsoft365ReadState State, IReadOnlyList<Microsoft365Member> Members);

public sealed record Microsoft365DeviceContext(
    string? TenantId,
    long SessionRevision,
    long Revision,
    IReadOnlyList<CachedEntraDevices> EntraReads,
    CachedIntuneDevices Intune,
    CachedMicrosoft365Members? RegisteredOwners);

public interface IMicrosoft365DeviceContextProvider
{
    Task<Result<Microsoft365DeviceContext>> ReadCachedAsync(string? tenantId, string? deviceObjectId,
        CancellationToken cancellationToken);
}
