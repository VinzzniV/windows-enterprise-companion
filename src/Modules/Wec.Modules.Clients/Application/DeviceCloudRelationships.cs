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
        List<Microsoft365Device> devices = [];
        foreach (CachedEntraDevices read in context.EntraReads)
        {
            devices.AddRange(read.Devices.Where(device => SameId(device.Id, reference.Id) && !devices.Contains(device)).ToArray());
        }
        return devices.ToArray();
    }

    internal static Microsoft365ManagedDevice[] PrimaryIntune(ObjectReference reference, Microsoft365DeviceContext context) =>
        reference.Source == ObjectSource.Intune ? IntuneEvidence(context).Where(device => SameId(device.Id, reference.Id)).ToArray() : [];

    private static Microsoft365ManagedDevice[] IntuneEvidence(Microsoft365DeviceContext context)
    {
        List<Microsoft365ManagedDevice> devices = [.. context.Intune.Devices];
        foreach (CachedIntuneDevices read in context.ManagedDetails)
        {
            devices.AddRange(read.Devices.Where(device => !devices.Contains(device)).ToArray());
        }
        return devices.ToArray();
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
        Microsoft365ManagedDevice[] managedEvidence = IntuneEvidence(context);
        if (entra.Length == 1)
        {
            if (UniqueEntraRegistration(context, entra[0]))
            {
                foreach (Microsoft365ManagedDevice managed in managedEvidence.Where(device => SameId(device.EntraDeviceId, entra[0].DeviceId)))
                {
                    Microsoft365ManagedDevice[] sameEnrollment = managedEvidence.Where(device => SameId(device.Id, managed.Id)).ToArray();
                    if (sameEnrollment.Length != 1) { continue; }
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
            Microsoft365Device[] matches = context.EntraReads.Where(read => read.State.Query.Resource == Microsoft365Resource.Devices)
                .SelectMany(read => read.Devices).Where(device => SameId(device.DeviceId, intune[0].EntraDeviceId)).ToArray();
            if (matches.Length == 1 && UniqueEntraRegistration(context, matches[0]))
            {
                Add(links, context.TenantId, ObjectKind.Device, ObjectSource.Entra, matches[0].Id, matches[0].DisplayName,
                    "Entra device record", "Exact azureADDeviceId / deviceId reference in the loaded directory set.");
            }
        }
        return links.Distinct().ToArray();
    }

    private static bool UniqueEntraRegistration(Microsoft365DeviceContext context, Microsoft365Device primary) =>
        context.EntraReads.Any(read => read.State.Query.Resource == Microsoft365Resource.Devices
            && read.State.Availability == Microsoft365Availability.Available && read.State.Coverage == EvidenceCoverage.ReturnedSet
            && read.Devices.Count(device => SameId(device.DeviceId, primary.DeviceId)) == 1
            && read.Devices.Any(device => SameId(device.Id, primary.Id) && SameId(device.DeviceId, primary.DeviceId)))
        && !context.EntraReads.Any(read => read.Devices.Count(device => SameId(device.DeviceId, primary.DeviceId)) > 1
            || read.Devices.Any(device => SameId(device.DeviceId, primary.DeviceId) && !SameId(device.Id, primary.Id)
                || SameId(device.Id, primary.Id) && !SameId(device.DeviceId, primary.DeviceId)));

    internal static IReadOnlyList<ObjectRelationship> Candidates(ObjectReference reference, Microsoft365DeviceContext context,
        IReadOnlyList<string> names, IReadOnlyList<ObjectRelationship> confirmed)
    {
        if (context.TenantId is null) { return []; }
        List<ObjectRelationship> candidates = [];
        Microsoft365ManagedDevice[] primaryIntune = PrimaryIntune(reference, context);
        Microsoft365Device[] primaryEntra = PrimaryEntra(reference, context);
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
            IdentityEvidence? name = NameEvidence(names, device.DeviceName);
            bool deviceId = primaryEntra.Any(entra => SameId(entra.DeviceId, device.EntraDeviceId));
            if (name is not null || deviceId)
            {
                Candidate(candidates, context.TenantId, ObjectSource.Intune, device.Id, device.DeviceName,
                    deviceId ? IdentityEvidence.Ambiguous : name!.Value, deviceId
                        ? "The registration ID matches but its uniqueness or enrollment evidence is incomplete or conflicting. Inspect this enrollment separately."
                        : "Device name candidate only; no Windows/AD to Intune identity is established.");
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
            "Associated user (Intune)", $"Intune enrollment {managed.Id} reports userId {managed.UserId}; this is not a primary-user or ownership claim.");

    private static void Add(List<ObjectRelationship> links, string scope, ObjectKind kind, ObjectSource source,
        string? id, string? label, string relation, string explanation)
    {
        if (Guid.TryParse(id, out Guid nativeId) && nativeId != Guid.Empty)
        {
            links.Add(new(new(kind, source, scope, nativeId.ToString("D")), label ?? id!, relation, IdentityEvidence.ScopedId, explanation));
        }
    }
}
