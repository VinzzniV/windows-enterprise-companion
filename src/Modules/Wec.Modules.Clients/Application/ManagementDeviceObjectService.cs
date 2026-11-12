using Microsoft.Extensions.Options;
using Wec.Core.Contracts;
using Wec.Core.Objects;
using Wec.Core.Results;

namespace Wec.Modules.Clients.Application;

internal sealed class ManagementDeviceObjectService(IManagementDeviceSnapshotProvider provider,
    WecWorkspaceIdentity workspace, IOptions<ObjectWorkingSetOptions> options)
{
    internal async Task<Result<ManagementDeviceObjectLists>> ListCachedAsync(ManagementDeviceListRequest request, CancellationToken cancellationToken)
    {
        string? search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();
        if (search is not null && (search.Length > 100 || search.Any(char.IsControl)))
        {
            return Result.Failure<ManagementDeviceObjectLists>(new(ErrorCode.InvalidRequest, "Use at most 100 search characters."));
        }
        ManagementDeviceSnapshot? snapshot = await provider.ReadCachedAsync(request.ActiveDirectory, request.Kaspersky, cancellationToken);
        ManagementDeviceSource[] sources = Enum.GetValues<ManagementDeviceSource>();
        int[] matches = new int[sources.Length];
        var candidates = sources.Select(_ => new List<ManagementDeviceListRow>()).ToArray();
        for (int sourceIndex = 0; sourceIndex < sources.Length; sourceIndex++)
        {
            if (snapshot is null || snapshot.SnapshotId == Guid.Empty) { continue; }
            ManagementDeviceSource source = sources[sourceIndex];
            for (int index = 0; index < Count(snapshot, source); index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ManagementDeviceListRow row = Row(snapshot, source, index);
                if (search is not null && !row.Label.Contains(search, StringComparison.OrdinalIgnoreCase)
                    && !row.Aliases.Any(alias => alias.Contains(search, StringComparison.OrdinalIgnoreCase))) { continue; }
                matches[sourceIndex]++;
                if (candidates[sourceIndex].Count < options.Value.MaximumRecords) { candidates[sourceIndex].Add(row); }
            }
        }
        int[] selected = new int[sources.Length];
        int remaining = options.Value.MaximumRecords;
        while (remaining > 0)
        {
            bool selectedAny = false;
            for (int index = 0; index < sources.Length && remaining > 0; index++)
            {
                if (selected[index] >= candidates[index].Count) { continue; }
                selected[index]++; remaining--; selectedAny = true;
            }
            if (!selectedAny) { break; }
        }
        ManagementDeviceListRead[] reads = sources.Select((source, index) => new ManagementDeviceListRead(source,
            State(snapshot, source), snapshot is null || snapshot.SnapshotId == Guid.Empty
                || State(snapshot, source).Availability is not ("Available" or "Partial" or "Truncated") ? null : matches[index],
            matches[index] > selected[index], candidates[index].Take(selected[index]).ToArray())).ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        return Result.Success(new ManagementDeviceObjectLists(workspace, snapshot?.SnapshotId, snapshot?.OpsiSessionId, snapshot?.SessionRevision ?? 0,
            snapshot?.Revision ?? 0, snapshot?.RetrievedAtUtc, options.Value.MaximumRecords, search, reads));
    }

    internal async Task<Result<ManagementDeviceRecordProfile>> ReadCachedAsync(ManagementDeviceRecordRequest request, CancellationToken cancellationToken)
    {
        ManagementDeviceRecordReference reference = request.Reference;
        if (reference.Workspace != workspace.Scope || reference.SnapshotId == Guid.Empty || !Enum.IsDefined(reference.Source) || reference.RecordIndex < 0)
        {
            return Result.Failure<ManagementDeviceRecordProfile>(new(ErrorCode.InvalidRequest, "Use a valid source-record reference from this WEC workspace."));
        }
        ManagementDeviceSnapshot? snapshot = await provider.ReadCachedAsync(request.ActiveDirectory, request.Kaspersky, cancellationToken);
        if (snapshot is null || snapshot.SnapshotId != reference.SnapshotId || reference.RecordIndex >= Count(snapshot, reference.Source))
        {
            return Result.Failure<ManagementDeviceRecordProfile>(Error.NotFound("This source snapshot is no longer available in the selected connection context. Read the source and select its record again."));
        }
        int index = reference.RecordIndex;
        var profile = new ManagementDeviceRecordProfile(Row(snapshot, reference.Source, index), State(snapshot, reference.Source), snapshot.RetrievedAtUtc,
            "This link identifies one observation in this in-memory source snapshot. It does not establish a permanent or cross-source device identity. Names and addresses remain candidate evidence.",
            reference.Source == ManagementDeviceSource.ActiveDirectory ? snapshot.ActiveDirectory[index] : null,
            reference.Source == ManagementDeviceSource.Kaspersky ? snapshot.Kaspersky[index] : null,
            reference.Source == ManagementDeviceSource.Opsi ? snapshot.Opsi[index] : null,
            reference.Source == ManagementDeviceSource.Nessus ? snapshot.Nessus[index] : null);
        cancellationToken.ThrowIfCancellationRequested();
        return Result.Success(profile);
    }

    private ManagementDeviceListRow Row(ManagementDeviceSnapshot snapshot, ManagementDeviceSource source, int index)
    {
        var reference = new ManagementDeviceRecordReference(workspace.Scope, snapshot.SnapshotId, source, index);
        if (source == ManagementDeviceSource.ActiveDirectory)
        {
            AdComputerInventoryItem row = snapshot.ActiveDirectory[index];
            ObjectReference? native = row.ObjectId is { } id && id != Guid.Empty && !string.IsNullOrWhiteSpace(row.DirectoryScope)
                ? new(ObjectKind.Device, ObjectSource.ActiveDirectory, row.DirectoryScope, id.ToString("D")) : null;
            return new(reference, native, row.DnsHostName ?? row.ComputerName,
                Aliases(row.ComputerName, row.DnsHostName, row.ObjectId?.ToString("D"), row.SecurityIdentifier), row.Enabled, row.OperatingSystem, row.SecurityIdentifier);
        }
        if (source == ManagementDeviceSource.Kaspersky)
        {
            KasperskyDeviceRecord row = snapshot.Kaspersky[index];
            return new(reference, null, row.Fqdn ?? row.ComputerName, Aliases(row.ComputerName, row.Fqdn, row.DnsName, row.RecordName), null, null, null);
        }
        if (source == ManagementDeviceSource.Opsi)
        {
            OpsiComputerInventoryItem row = snapshot.Opsi[index];
            return new(reference, null, row.ComputerName, Aliases(row.ComputerName), null, null, null);
        }
        NessusComputerInventoryItem nessus = snapshot.Nessus[index];
        return new(reference, null, nessus.Fqdn ?? nessus.ComputerName,
            Aliases(nessus.ComputerName, nessus.Fqdn, nessus.IpAddress, nessus.SourceKey, nessus.AssetId, nessus.HostUuid, nessus.BiosUuid), null, null, null);
    }

    private static string[] Aliases(params string?[] values) => values.Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static int Count(ManagementDeviceSnapshot snapshot, ManagementDeviceSource source) => source switch
    {
        ManagementDeviceSource.ActiveDirectory => snapshot.ActiveDirectory.Count,
        ManagementDeviceSource.Kaspersky => snapshot.Kaspersky.Count,
        ManagementDeviceSource.Opsi => snapshot.Opsi.Count,
        ManagementDeviceSource.Nessus => snapshot.Nessus.Count,
        _ => 0,
    };

    private static ManagementDeviceSourceState State(ManagementDeviceSnapshot? snapshot, ManagementDeviceSource source) =>
        snapshot?.Sources.SingleOrDefault(state => state.Source == source.ToString())
        ?? new(source.ToString(), null, "NotLoaded", null, 0);
}
