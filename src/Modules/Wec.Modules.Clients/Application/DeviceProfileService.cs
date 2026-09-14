using Wec.Core.Contracts;
using Wec.Core.Microsoft365;
using Wec.Core.Objects;
using Wec.Core.Results;
using Wec.Core.Targets;

namespace Wec.Modules.Clients.Application;

internal sealed class DeviceProfileService(WecWorkspaceIdentity workspace, ClientOverviewService localOverview,
    IInventoryClientSnapshotProvider storedHosts, ISavedClientTargetProvider savedTargets,
    IDirectoryComputerReadProvider directory, IManagementDeviceSnapshotProvider management,
    IMicrosoft365DeviceContextProvider cloud)
{
    internal async Task<Result<DeviceProfileResult>> GetAsync(DeviceProfileRequest request, CancellationToken cancellationToken)
    {
        ObjectReference reference = request.Reference;
        if (reference.Kind != ObjectKind.Device || !Enum.IsDefined(reference.Source)
            || string.IsNullOrWhiteSpace(reference.Scope) || reference.Scope.Length > 253
            || string.IsNullOrWhiteSpace(reference.Id) || reference.Id.Length > 253 || reference.Id.Any(char.IsControl)
            || reference.Source != ObjectSource.Wec && (!Guid.TryParse(reference.Id, out Guid id) || id == Guid.Empty))
        {
            return Invalid("Use a valid scoped device reference.");
        }
        if (reference.Source == ObjectSource.Wec && reference.Scope != workspace.Scope)
        {
            return Invalid("This stored target belongs to another WEC workspace. Open it in the matching workspace.");
        }
        if (reference.Source is ObjectSource.Entra or ObjectSource.Intune
            && (!Guid.TryParse(reference.Scope, out Guid tenant) || tenant == Guid.Empty))
        {
            return Invalid("A valid tenant is required for a cloud device.");
        }
        reference = reference with
        {
            Id = reference.Source == ObjectSource.Wec ? reference.Id : Guid.Parse(reference.Id).ToString("D"),
            Scope = reference.Source == ObjectSource.ActiveDirectory ? reference.Scope.Trim().TrimEnd('.').ToLowerInvariant()
                : reference.Source == ObjectSource.Wec ? reference.Scope : Guid.Parse(reference.Scope).ToString("D"),
        };
        List<Error> errors = [];
        List<ObjectRelationship> candidates = [];
        IReadOnlyList<string> hosts = (await storedHosts.ListHostsAsync(cancellationToken)).Select(item => item.Host)
            .Concat((await savedTargets.ListClientsAsync(cancellationToken)).Select(item => item.Host))
            .Append(workspace.LocalComputerName).Where(host => !string.IsNullOrWhiteSpace(host))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        string? operationalHost = null;
        if (reference.Source == ObjectSource.Wec)
        {
            string[] exact = hosts.Where(host => string.Equals(host, reference.Id, StringComparison.OrdinalIgnoreCase)).ToArray();
            string[] possible = hosts.Where(host => DeviceCloudRelationships.NameEvidence([reference.Id], host) is not null).ToArray();
            if (exact.Length == 1) { operationalHost = exact[0]; }
            else if (possible.Length == 0) { operationalHost = reference.Id; }
            else { candidates.AddRange(possible.Select(host => WecCandidate(host, "Select the exact stored target; an alias does not identify its snapshot."))); }
        }
        ClientOverviewResult? wec = operationalHost is null ? null : await localOverview.GetAsync(operationalHost, cancellationToken);
        ManagementDeviceSnapshot? sourceRecords = await management.ReadCachedAsync(request.ActiveDirectory, request.Kaspersky, cancellationToken);
        CachedDirectoryComputer? ad = null;
        AdComputerInventoryItem[] adRecords = [];
        if (reference.Source == ObjectSource.ActiveDirectory)
        {
            Result<DirectoryUserReadConnection> connection = DirectoryConnection(request.ActiveDirectory);
            if (connection.IsFailure) { return Result.Failure<DeviceProfileResult>(connection.Error!); }
            var query = new DirectoryComputerIdentityQuery(connection.Value with { Domain = connection.Value.Domain ?? reference.Scope }, reference.Scope, Guid.Parse(reference.Id));
            if (request.LoadDirectoryIdentity)
            {
                Result<DirectoryComputerIdentityResult> loaded = await directory.ReadIdentityAsync(query, cancellationToken);
                if (loaded.IsFailure) { errors.Add(loaded.Error!); }
            }
            ad = await directory.ReadCachedAsync(query, cancellationToken);
            adRecords = ad?.Data?.Computers.ToArray() ?? sourceRecords?.ActiveDirectory.Where(item => item.ObjectId == query.ObjectId
                && string.Equals(item.DirectoryScope, reference.Scope, StringComparison.OrdinalIgnoreCase)).ToArray() ?? [];
        }
        Result<Microsoft365DeviceContext> cloudRead = await cloud.ReadCachedAsync(
            reference.Source is ObjectSource.Entra or ObjectSource.Intune ? reference.Scope : null,
            reference.Source == ObjectSource.Entra ? reference.Id : null, cancellationToken,
            reference.Source == ObjectSource.Intune ? reference.Id : null);
        if (cloudRead.IsFailure && reference.Source is ObjectSource.Entra or ObjectSource.Intune)
        {
            return Result.Failure<DeviceProfileResult>(cloudRead.Error!);
        }
        if (cloudRead.IsFailure) { errors.Add(cloudRead.Error!); }
        Microsoft365DeviceContext? cloudContext = cloudRead.IsSuccess ? cloudRead.Value : null;
        Microsoft365Device[] primaryEntra = cloudContext is null ? [] : DeviceCloudRelationships.PrimaryEntra(reference, cloudContext);
        Microsoft365ManagedDevice[] primaryIntune = cloudContext is null ? [] : DeviceCloudRelationships.PrimaryIntune(reference, cloudContext);
        string[] names = new[] { operationalHost }
            .Concat(adRecords.SelectMany(item => new[] { item.ComputerName, item.DnsHostName }))
            .Concat(primaryEntra.Select(item => item.DisplayName)).Concat(primaryIntune.Select(item => item.DeviceName))
            .Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (string host in hosts.Where(host => !string.Equals(host, operationalHost, StringComparison.OrdinalIgnoreCase)
            && DeviceCloudRelationships.NameEvidence(names, host) is not null))
        {
            candidates.Add(WecCandidate(host, "Address/name candidate only. Stored Windows evidence does not confirm a cloud or directory device identity."));
        }
        ManagementDeviceSnapshot? related = sourceRecords is null ? null : sourceRecords with
        {
            ActiveDirectory = sourceRecords.ActiveDirectory.Where(item => DeviceCloudRelationships.NameEvidence(names, item.DnsHostName ?? item.ComputerName) is not null).ToArray(),
            Kaspersky = sourceRecords.Kaspersky.Where(item => new[] { item.ComputerName, item.Fqdn, item.DnsName }
                .Any(name => DeviceCloudRelationships.NameEvidence(names, name) is not null)).ToArray(),
            Opsi = sourceRecords.Opsi.Where(item => DeviceCloudRelationships.NameEvidence(names, item.ComputerName) is not null).ToArray(),
            Nessus = sourceRecords.Nessus.Where(item => new[] { item.ComputerName, item.Fqdn, item.IpAddress }
                .Any(name => DeviceCloudRelationships.NameEvidence(names, name) is not null)).ToArray(),
        };
        foreach (AdComputerInventoryItem item in related?.ActiveDirectory ?? [])
        {
            if (item.ObjectId is not { } objectId || string.IsNullOrWhiteSpace(item.DirectoryScope)) { continue; }
            var target = new ObjectReference(ObjectKind.Device, ObjectSource.ActiveDirectory, item.DirectoryScope, objectId.ToString("D"));
            if (target != reference)
            {
                candidates.Add(new(target, item.DnsHostName ?? item.ComputerName, "Possible AD computer",
                    DeviceCloudRelationships.NameEvidence(names, item.DnsHostName ?? item.ComputerName) ?? IdentityEvidence.Unresolved,
                    "Name evidence only. Open the scoped AD identity to inspect it independently."));
            }
        }
        IReadOnlyList<ObjectRelationship> relationships = cloudContext is null ? [] : DeviceCloudRelationships.Confirmed(reference, cloudContext);
        if (cloudContext is not null) { candidates.AddRange(DeviceCloudRelationships.Candidates(reference, cloudContext, names, relationships)); }
        int primaryCount = reference.Source switch
        {
            ObjectSource.Wec => operationalHost is null ? 0 : 1,
            ObjectSource.ActiveDirectory => adRecords.Length,
            ObjectSource.Entra => primaryEntra.Length,
            ObjectSource.Intune => primaryIntune.Length,
            _ => 0,
        };
        IdentityEvidence identity = primaryCount > 1 || reference.Source == ObjectSource.Wec && operationalHost is null
            ? IdentityEvidence.Ambiguous : primaryCount == 0 ? IdentityEvidence.Unresolved
            : reference.Source == ObjectSource.Wec ? IdentityEvidence.AddressCandidate : IdentityEvidence.ScopedId;
        bool conflictingCloudId = primaryEntra.Length == 1 && cloudContext!.EntraReads.SelectMany(read => read.Devices)
            .Any(device => DeviceCloudRelationships.SameId(device.DeviceId, primaryEntra[0].DeviceId)
                && !DeviceCloudRelationships.SameId(device.Id, primaryEntra[0].Id));
        if (conflictingCloudId) { identity = IdentityEvidence.Conflict; }
        string title = reference.Source switch
        {
            ObjectSource.Wec => reference.Id,
            ObjectSource.ActiveDirectory when adRecords.Length == 1 => adRecords[0].DnsHostName ?? adRecords[0].ComputerName,
            ObjectSource.Entra when primaryEntra.Length == 1 => primaryEntra[0].DisplayName ?? reference.Id,
            ObjectSource.Intune when primaryIntune.Length == 1 => primaryIntune[0].DeviceName ?? reference.Id,
            _ => reference.Id,
        };
        string explanation = identity switch
        {
            IdentityEvidence.Conflict => "Different Entra object IDs share the same deviceId in loaded evidence. Intune relationships are not confirmed automatically.",
            IdentityEvidence.Ambiguous => "Multiple or ambiguous source records remain separate. No operational target is selected automatically.",
            IdentityEvidence.Unresolved => "This scoped record has not been resolved in the available evidence. Load its source explicitly; missing evidence does not prove absence.",
            IdentityEvidence.AddressCandidate => "Exact WEC target address. This identifies stored target evidence, not a stable device identity across rename or reinstallation.",
            _ => "Anchored to this source's scoped object ID. Other source records are related only by explicit ID evidence; names remain candidates.",
        };
        cancellationToken.ThrowIfCancellationRequested();
        return Result.Success(new DeviceProfileResult(reference, title, identity, explanation, operationalHost, wec, ad,
            adRecords, cloudContext is null ? null : DeviceCloudRelationships.Filter(reference, cloudContext, names), related,
            relationships, candidates.Distinct().ToArray(), errors));
    }

    private ObjectRelationship WecCandidate(string host, string explanation) => new(
        new(ObjectKind.Device, ObjectSource.Wec, workspace.Scope, host), host, "Stored Windows target",
        IdentityEvidence.AddressCandidate, explanation);

    private static Result<DirectoryUserReadConnection> DirectoryConnection(DirectoryInventoryConnection? connection)
    {
        ScanCredentials credentials = ScanCredentials.CurrentUser;
        if (!string.IsNullOrWhiteSpace(connection?.UserName))
        {
            if (connection.Password is null) { return Result.Failure<DirectoryUserReadConnection>(new(ErrorCode.InvalidRequest, "Explicit directory credentials require a password.")); }
            Result<ScanCredentials> normalized = DirectoryScanCredentials.NormalizeExplicit(connection.UserName, connection.UserDomain, connection.Password, connection.Domain);
            if (normalized.IsFailure) { return Result.Failure<DirectoryUserReadConnection>(normalized.Error!); }
            credentials = normalized.Value;
        }
        return Result.Success(new DirectoryUserReadConnection(connection?.Domain, connection?.Server, credentials));
    }

    private static Result<DeviceProfileResult> Invalid(string message) => Result.Failure<DeviceProfileResult>(new(ErrorCode.InvalidRequest, message));
}
