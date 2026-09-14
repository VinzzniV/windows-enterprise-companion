using Wec.Core.Contracts;
using Wec.Core.Microsoft365;
using Wec.Core.Objects;

namespace Wec.Modules.UserManagement.Application;

internal static class UserAccountRelationships
{
    internal static bool SameId(string? a, string? b) => Guid.TryParse(a, out Guid left) && left != Guid.Empty
        && Guid.TryParse(b, out Guid right) && left == right;

    internal static Microsoft365User[] Primary(string id, Microsoft365UserContext context)
    {
        CachedEntraUsers? detail = context.UserReads.FirstOrDefault(read => read.State.Query.Resource == Microsoft365Resource.User
            && SameId(read.State.Query.ObjectId, id) && read.State.Availability == Microsoft365Availability.Available);
        if (detail is not null) { return detail.Users.Where(user => SameId(user.Id, id)).ToArray(); }
        Microsoft365User[] inventory = context.UserReads.Where(read => read.State.Query.Resource == Microsoft365Resource.Users)
            .SelectMany(read => read.Users).Where(user => SameId(user.Id, id)).ToArray();
        return inventory.Length > 0 ? inventory : context.UserReads.Where(read => read.State.Query.Resource == Microsoft365Resource.UsersBySid)
            .SelectMany(read => read.Users).Where(user => SameId(user.Id, id)).ToArray();
    }

    internal static bool HasUniqueSid(Microsoft365UserContext context, string? value, string? userId)
    {
        string? sid = Microsoft365QueryValidation.AccountSid(value);
        if (sid is null || !Guid.TryParse(userId, out Guid id) || id == Guid.Empty) { return false; }
        CachedEntraUsers[] applicable = context.UserReads.Where(read => read.State.Availability == Microsoft365Availability.Available
            && (read.State.Query.Resource == Microsoft365Resource.Users
                || read.State.Query.Resource == Microsoft365Resource.UsersBySid && read.State.Query.SecurityIdentifier == sid)).ToArray();
        bool completeUnique = applicable.Any(read => read.State.Coverage == EvidenceCoverage.ReturnedSet
            && read.Users.Count(user => Microsoft365QueryValidation.AccountSid(user.OnPremisesSid) == sid) == 1
            && read.Users.Any(user => SameId(user.Id, userId) && Microsoft365QueryValidation.AccountSid(user.OnPremisesSid) == sid));
        return completeUnique && !context.UserReads.Any(read =>
            read.Users.Count(user => Microsoft365QueryValidation.AccountSid(user.OnPremisesSid) == sid) > 1
            || read.Users.Any(user => Microsoft365QueryValidation.AccountSid(user.OnPremisesSid) == sid && !SameId(user.Id, userId))
            || read.Users.Any(user => SameId(user.Id, userId)
                && Microsoft365QueryValidation.AccountSid(user.OnPremisesSid) is { } otherSid && otherSid != sid));
    }

    internal static IReadOnlyList<ObjectRelationship> Candidates(DirectoryUserRecord? ad, Microsoft365UserContext context)
    {
        if (ad is null || context.TenantId is null) { return []; }
        string? sid = Microsoft365QueryValidation.AccountSid(ad.Sid);
        return context.UserReads.SelectMany(read => read.Users).Where(user => Guid.TryParse(user.Id, out Guid id) && id != Guid.Empty)
            .Where(user => sid is not null && Microsoft365QueryValidation.AccountSid(user.OnPremisesSid) == sid || SameUpn(ad.UserPrincipalName, user.UserPrincipalName))
            .Select(user =>
            {
                string? cloudSid = Microsoft365QueryValidation.AccountSid(user.OnPremisesSid);
                bool conflict = sid is not null && cloudSid is not null && sid != cloudSid;
                bool exact = sid is not null && sid == cloudSid;
                bool confirmed = exact && HasUniqueSid(context, sid, user.Id);
                return new ObjectRelationship(new(ObjectKind.User, ObjectSource.Entra, context.TenantId, Guid.Parse(user.Id!).ToString("D")),
                    user.DisplayName ?? user.UserPrincipalName ?? user.Id!, "Entra account",
                    conflict ? IdentityEvidence.Conflict : confirmed ? IdentityEvidence.ScopedId : exact ? IdentityEvidence.Ambiguous : IdentityEvidence.AliasCandidate,
                    conflict ? "The UPN matches but the account SIDs differ. These accounts are not joined."
                        : confirmed ? "Exact AD objectSid / Entra onPremisesSecurityIdentifier in the selected directory and tenant; unique in applicable evidence."
                        : exact ? "Matching SID in incomplete or conflicting evidence. Read the exact SID query to establish uniqueness."
                        : "UPN candidate only. Mutable names cannot prove account identity.");
            }).Distinct().ToArray();
    }

    internal static Microsoft365UserContext Filter(Microsoft365UserContext context, Microsoft365User? primary,
        IReadOnlyList<ObjectRelationship> candidates) => context with
    {
        UserReads = context.UserReads.Select(read => read with { Users = read.Users.Where(user =>
            primary is not null && SameId(primary.Id, user.Id) || candidates.Any(candidate => SameId(candidate.Target.Id, user.Id))).ToArray() }).ToArray(),
    };

    internal static IReadOnlyList<ObjectRelationship> CloudLinks(Microsoft365UserContext context)
    {
        if (context.TenantId is null) { return []; }
        List<ObjectRelationship> links = [];
        foreach (Microsoft365Group group in context.DirectGroups?.Groups ?? [])
        {
            Add(ObjectKind.Group, ObjectSource.Entra, group.Id, group.DisplayName, "Direct group (Entra)", "Direct memberOf result; no transitive access or effective-permission claim.");
        }
        foreach (Microsoft365Device device in context.RegisteredDevices?.Devices ?? [])
        {
            Add(ObjectKind.Device, ObjectSource.Entra, device.Id, device.DisplayName, "Registered device (Entra)", "Explicit registeredDevices relationship; not physical ownership or a Windows login.");
        }
        foreach (CachedIntuneDevices read in context.AssociatedIntune)
        {
            foreach (Microsoft365ManagedDevice device in read.Devices)
            {
                Add(ObjectKind.Device, ObjectSource.Intune, device.Id, device.DeviceName, "Associated user (Intune)", "Exact Intune userId references this Entra account. This is not a primary-user or ownership claim.");
            }
        }
        return links.Distinct().ToArray();

        void Add(ObjectKind kind, ObjectSource source, string? id, string? label, string relation, string explanation)
        {
            if (Guid.TryParse(id, out Guid parsed) && parsed != Guid.Empty)
            {
                links.Add(new(new(kind, source, context.TenantId, parsed.ToString("D")), label ?? id!, relation, IdentityEvidence.ScopedId, explanation));
            }
        }
    }

    private static bool SameUpn(string? a, string? b) => !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
        && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
