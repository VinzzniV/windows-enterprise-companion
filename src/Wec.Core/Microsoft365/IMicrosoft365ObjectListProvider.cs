using Wec.Core.Objects;
using Wec.Core.Results;

namespace Wec.Core.Microsoft365;

public sealed record Microsoft365ObjectListRow(ObjectKind Kind, ObjectSource Source, string? ObjectId,
    string? DisplayName, string? UserPrincipalName, bool? AccountEnabled, string? OperatingSystem,
    string? SecurityIdentifier, string? RegistrationDeviceId, string? AssociatedUserId,
    IReadOnlyList<string>? AssignedSkuIds, string? ComplianceState = null, string? ManagementState = null,
    string? Department = null, bool? SecurityEnabled = null, bool? MailEnabled = null, string? GroupTypes = null);

public sealed record Microsoft365ObjectListRead(Microsoft365ReadState State, IReadOnlyList<Microsoft365ObjectListRow> Rows);

public sealed record Microsoft365ObjectLists(string? TenantId, long SessionRevision, long Revision,
    int RecordLimit, int CachedSourceRecords, int LoadedSourceRecords, bool Truncated,
    IReadOnlyList<Microsoft365ObjectListRead> Reads);

public interface IMicrosoft365ObjectListProvider
{
    Task<Result<Microsoft365ObjectLists>> ReadObjectListsCachedAsync(string? tenantId, CancellationToken cancellationToken);
}
