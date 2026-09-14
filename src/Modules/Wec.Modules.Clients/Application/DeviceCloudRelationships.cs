using Wec.Core.Microsoft365;
using Wec.Core.Objects;
using Wec.Core.Targets;

namespace Wec.Modules.Clients.Application;

internal static class DeviceCloudRelationships
{
    internal static bool SameId(string? left, string? right) => Guid.TryParse(left, out Guid a) && a != Guid.Empty
        && Guid.TryParse(right, out Guid b) && b != Guid.Empty && a == b;

    internal static Microsoft365Device[] PrimaryEntra(ObjectReference reference, Microsoft365DeviceContext context)
    {
        if (reference.Source != ObjectSource.Entra) { return []; }
        CachedEntraDevices? detail = context.EntraReads.FirstOrDefault(read => read.State.Query.Resource == Microsoft365Resource.Device
            && SameId(read.State.Query.ObjectId, reference.Id) && read.State.Availability == Microsoft365Availability.Available);
        IEnumerable<Microsoft365Device> devices = detail?.Devices
            ?? context.EntraReads.Where(read => read.State.Query.Resource == Microsoft365Resource.Devices).SelectMany(read => read.Devices);
        return devices.Where(device => SameId(device.Id, reference.Id)).ToArray();
    }

    internal static Microsoft365ManagedDevice[] PrimaryIntune(ObjectReference reference, Microsoft365DeviceContext context) =>
        reference.Source == ObjectSource.Intune ? IntuneEvidence(context).Where(device => SameId(device.Id, reference.Id)).ToArray() : [];

    private static Microsoft365ManagedDevice[] IntuneEvidence(Microsoft365DeviceContext context)
    {
        CachedIntuneDevices[] details = context.ManagedDetails.Where(read => read.State.Availability == Microsoft365Availability.Available).ToArray();
        return context.Intune.Devices.Where(device => !details.Any(read => SameId(read.State.Query.ObjectId, device.Id)))
            .Concat(details.SelectMany(read => read.Devices)).ToArray();
    }

    internal static Microsoft365DeviceContext Filter(ObjectReference reference, Microsoft365DeviceContext context, IReadOnlyList<string> names)
    {
        Microsoft365Device[] entra = PrimaryEntra(reference, context);
        Microsoft365ManagedDevice[] intune = PrimaryIntune(reference, context);
        return context with
        {
            ManagedDetails = context.ManagedDetails.Select(read => read with
            {
                Devices = read.Devices.Where(device => reference.Source == ObjectSource.Intune && SameId(device.Id, reference.Id)
                    || entra.Any(directory => SameId(directory.DeviceId, device.EntraDeviceId)) || NameEvidence(names, device.DeviceName) is not null).ToArray(),
            }).ToArray(),
            EntraReads = context.EntraReads.Select(read => read with
            {
                Devices = read.Devices.Where(device => reference.Source == ObjectSource.Entra && SameId(device.Id, reference.Id)
                    || intune.Any(managed => SameId(managed.EntraDeviceId, device.DeviceId)) || NameEvidence(names, device.DisplayName) is not null).ToArray(),
            }).ToArray(),
            Intune = context.Intune with
            {
                Devices = context.Intune.Devices.Where(device => reference.Source == ObjectSource.Intune && SameId(device.Id, reference.Id)
                    || entra.Any(directory => SameId(directory.DeviceId, device.EntraDeviceId)) || NameEvidence(names, device.DeviceName) is not null).ToArray(),
            },
        };
    }

    internal static IReadOnlyList<ObjectRelationship> Confirmed(ObjectReference reference, Microsoft365DeviceContext context)
    {
        if (context.TenantId is null) { return []; }
        List<ObjectRelationship> links = [];
        Microsoft365Device[] entra = PrimaryEntra(reference, context);
        Microsoft365ManagedDevice[] intune = PrimaryIntune(reference, context);
        if (entra.Length == 1)
        {
            Microsoft365Device[] conflictingIds = context.EntraReads.SelectMany(read => read.Devices)
                .Where(device => SameId(device.DeviceId, entra[0].DeviceId) && !SameId(device.Id, entra[0].Id)).ToArray();
            if (conflictingIds.Length == 0)
            {
                foreach (Microsoft365ManagedDevice managed in IntuneEvidence(context).Where(device => SameId(device.EntraDeviceId, entra[0].DeviceId)))
                {
                    Add(links, context.TenantId, ObjectKind.Device, ObjectSource.Intune, managed.Id, managed.DeviceName,
                        "Intune enrollment record", "Exact deviceId / azureADDeviceId reference. Multiple enrollment records remain separate.");
                    AddAssociatedUser(links, context.TenantId, managed);
                }
            }
            if (context.RegisteredOwners is { } owners)
            {
                foreach (Microsoft365Member owner in owners.Members)
                {
                    ObjectKind? kind = owner.ObjectType switch { "user" or "#microsoft.graph.user" => ObjectKind.User, "group" or "#microsoft.graph.group" => ObjectKind.Group, _ => null };
                    if (kind is { } ownerKind)
                    {
                        Add(links, context.TenantId, ownerKind, ObjectSource.Entra, owner.Id, owner.DisplayName,
                            "Registered owner (Entra)", "Explicit registeredOwners relationship; it does not establish a Windows login or an Intune primary user.");
                    }
                }
            }
        }
        if (intune.Length == 1)
        {
            AddAssociatedUser(links, context.TenantId, intune[0]);
            CachedEntraDevices? inventory = context.EntraReads.SingleOrDefault(read => read.State.Query.Resource == Microsoft365Resource.Devices);
            Microsoft365Device[] matches = inventory?.Devices.Where(device => SameId(device.DeviceId, intune[0].EntraDeviceId)).ToArray() ?? [];
            if (matches.Length == 1 && inventory?.State.Coverage == EvidenceCoverage.ReturnedSet)
            {
                Add(links, context.TenantId, ObjectKind.Device, ObjectSource.Entra, matches[0].Id, matches[0].DisplayName,
                    "Entra device record", "Exact azureADDeviceId / deviceId reference in the loaded directory set.");
            }
        }
        return links.Distinct().ToArray();
    }

    internal static IReadOnlyList<ObjectRelationship> Candidates(ObjectReference reference, Microsoft365DeviceContext context,
        IReadOnlyList<string> names, IReadOnlyList<ObjectRelationship> confirmed)
    {
        if (context.TenantId is null) { return []; }
        List<ObjectRelationship> candidates = [];
        Microsoft365ManagedDevice[] primaryIntune = PrimaryIntune(reference, context);
        foreach (Microsoft365Device device in context.EntraReads.SelectMany(read => read.Devices))
        {
            if (reference.Source == ObjectSource.Entra && SameId(device.Id, reference.Id)) { continue; }
            IdentityEvidence? name = NameEvidence(names, device.DisplayName);
            bool deviceId = primaryIntune.Any(managed => SameId(managed.EntraDeviceId, device.DeviceId));
            if (name is not null || deviceId)
            {
                Candidate(candidates, context.TenantId, ObjectSource.Entra, device.Id, device.DisplayName,
                    deviceId ? IdentityEvidence.Ambiguous : name!.Value,
                    deviceId ? "The device ID occurs in incomplete or ambiguous directory evidence; select a source record explicitly."
                        : "Device names are mutable and do not prove that this is the same device.");
            }
        }
        foreach (Microsoft365ManagedDevice device in IntuneEvidence(context))
        {
            if (reference.Source == ObjectSource.Intune && SameId(device.Id, reference.Id)) { continue; }
            if (NameEvidence(names, device.DeviceName) is { } evidence)
            {
                Candidate(candidates, context.TenantId, ObjectSource.Intune, device.Id, device.DeviceName, evidence,
                    "Device name candidate only; no Windows/AD to Intune identity is established.");
            }
        }
        return candidates.Where(candidate => !confirmed.Any(link => link.Target == candidate.Target)).Distinct().ToArray();
    }

    internal static IdentityEvidence? NameEvidence(IEnumerable<string> names, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) { return null; }
        string key = HostAddress.ComparisonKey(value);
        string? alias = HostAddress.ShortNameAlias(value);
        if (names.Any(name => HostAddress.ComparisonKey(name) == key)) { return IdentityEvidence.AddressCandidate; }
        return alias is not null && names.Any(name => HostAddress.ShortNameAlias(name) == alias) ? IdentityEvidence.AliasCandidate : null;
    }

    private static void Candidate(List<ObjectRelationship> links, string scope, ObjectSource source, string? id,
        string? label, IdentityEvidence evidence, string explanation)
    {
        if (Guid.TryParse(id, out Guid nativeId) && nativeId != Guid.Empty)
        {
            links.Add(new(new(ObjectKind.Device, source, scope, nativeId.ToString("D")), label ?? id!, "Possible device record", evidence, explanation));
        }
    }

    private static void AddAssociatedUser(List<ObjectRelationship> links, string scope, Microsoft365ManagedDevice managed) =>
        Add(links, scope, ObjectKind.User, ObjectSource.Entra, managed.UserId, managed.UserPrincipalName,
            "Associated user (Intune)", "Intune's reported userId; this is not a primary-user or ownership claim.");

    private static void Add(List<ObjectRelationship> links, string scope, ObjectKind kind, ObjectSource source,
        string? id, string? label, string relation, string explanation)
    {
        if (Guid.TryParse(id, out Guid nativeId) && nativeId != Guid.Empty)
        {
            links.Add(new(new(kind, source, scope, nativeId.ToString("D")), label ?? id!, relation, IdentityEvidence.ScopedId, explanation));
        }
    }
}
