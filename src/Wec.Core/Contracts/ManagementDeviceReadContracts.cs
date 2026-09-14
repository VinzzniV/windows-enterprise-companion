namespace Wec.Core.Contracts;

public sealed record DirectoryInventoryConnection(
    string? Domain = null,
    string? Server = null,
    string? UserName = null,
    string? UserDomain = null,
    string? Password = null);

public sealed record KasperskyInventoryConnection(
    string? Server = null,
    int? Port = null,
    string? UserName = null,
    string? Domain = null,
    string? Password = null);

public sealed record ManagementDeviceSourceState(
    string Source,
    string? Scope,
    string Availability,
    string? Error,
    int LoadedRecords);

public sealed record KasperskyDeviceRecord(
    string ComputerName,
    string? Fqdn,
    string? DnsName,
    string? RecordName,
    DateTimeOffset? LastSeen,
    string? AgentVersion,
    string? KesVersion,
    string? AdministrationGroup);

public sealed record ManagementDeviceSnapshot(
    DateTimeOffset RetrievedAtUtc,
    IReadOnlyList<ManagementDeviceSourceState> Sources,
    IReadOnlyList<AdComputerInventoryItem> ActiveDirectory,
    IReadOnlyList<KasperskyDeviceRecord> Kaspersky,
    IReadOnlyList<OpsiComputerInventoryItem> Opsi,
    IReadOnlyList<NessusComputerInventoryItem> Nessus)
{
    public long SessionRevision { get; init; }
    public long Revision { get; init; }
}

public interface IManagementDeviceSnapshotProvider
{
    Task<ManagementDeviceSnapshot?> ReadCachedAsync(
        DirectoryInventoryConnection? activeDirectory,
        KasperskyInventoryConnection? kaspersky,
        CancellationToken cancellationToken);
}
