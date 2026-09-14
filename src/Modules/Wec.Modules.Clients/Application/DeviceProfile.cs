using Wec.Core.Contracts;
using Wec.Core.Microsoft365;
using Wec.Core.Objects;
using Wec.Core.Results;

namespace Wec.Modules.Clients.Application;

public sealed record DeviceProfileRequest(ObjectReference Reference,
    DirectoryInventoryConnection? ActiveDirectory = null, KasperskyInventoryConnection? Kaspersky = null,
    bool LoadDirectoryIdentity = false);

public sealed record DeviceProfileResult(
    ObjectReference Reference,
    string Title,
    IdentityEvidence Identity,
    string Explanation,
    string? OperationalHost,
    ClientOverviewResult? Wec,
    CachedDirectoryComputer? Directory,
    IReadOnlyList<AdComputerInventoryItem> DirectoryRecords,
    Microsoft365DeviceContext? Cloud,
    ManagementDeviceSnapshot? ManagementCandidates,
    IReadOnlyList<ObjectRelationship> Relationships,
    IReadOnlyList<ObjectRelationship> Candidates,
    IReadOnlyList<Error> SourceErrors)
{
    public IReadOnlyList<StoredDeviceCandidateSource> StoredCandidateSources { get; init; } = [];
}

public sealed record StoredDeviceCandidateSource(StoredDeviceListSource Source, string? Search,
    int LoadedRecords, int? TotalRecords, Error? Error);
