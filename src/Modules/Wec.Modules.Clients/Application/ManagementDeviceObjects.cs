using Wec.Core.Contracts;
using Wec.Core.Objects;

namespace Wec.Modules.Clients.Application;

public enum ManagementDeviceSource { ActiveDirectory, Kaspersky, Opsi, Nessus }

public sealed record ManagementDeviceRecordReference(string Workspace, Guid SnapshotId, ManagementDeviceSource Source, int RecordIndex);
public sealed record ManagementDeviceListRow(ManagementDeviceRecordReference Reference, ObjectReference? NativeReference,
    string Label, IReadOnlyList<string> Aliases, bool? AccountEnabled, string? OperatingSystem, string? SecurityIdentifier);
public sealed record ManagementDeviceListRead(ManagementDeviceSource Source, ManagementDeviceSourceState State,
    int? MatchingCachedRecords, bool Limited, IReadOnlyList<ManagementDeviceListRow> Rows);
public sealed record ManagementDeviceObjectLists(WecWorkspaceIdentity Workspace, Guid? SnapshotId, Guid? OpsiSessionId, long SessionRevision,
    long Revision, DateTimeOffset? RetrievedAtUtc, int MaximumRecords, string? Search, IReadOnlyList<ManagementDeviceListRead> Reads);
public sealed record ManagementDeviceListRequest(DirectoryInventoryConnection? ActiveDirectory = null,
    KasperskyInventoryConnection? Kaspersky = null, string? Search = null);
public sealed record ManagementDeviceRecordRequest(ManagementDeviceRecordReference Reference,
    DirectoryInventoryConnection? ActiveDirectory = null, KasperskyInventoryConnection? Kaspersky = null);
public sealed record ManagementDeviceRecordProfile(ManagementDeviceListRow Record, ManagementDeviceSourceState SourceState,
    DateTimeOffset RetrievedAtUtc, string IdentityExplanation, AdComputerInventoryItem? ActiveDirectory,
    KasperskyDeviceRecord? Kaspersky, OpsiComputerInventoryItem? Opsi, NessusComputerInventoryItem? Nessus);
