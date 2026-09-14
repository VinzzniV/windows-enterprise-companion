using Wec.Core.Results;

namespace Wec.Core.Microsoft365;

public sealed record Microsoft365GroupContext(string? TenantId, long SessionRevision, long Revision,
    IReadOnlyList<CachedMicrosoft365Groups> GroupReads, CachedMicrosoft365Members? DirectMembers);

public interface IMicrosoft365GroupContextProvider
{
    Task<Result<Microsoft365GroupContext>> ReadGroupCachedAsync(string? tenantId, string? groupObjectId, CancellationToken cancellationToken);
}
