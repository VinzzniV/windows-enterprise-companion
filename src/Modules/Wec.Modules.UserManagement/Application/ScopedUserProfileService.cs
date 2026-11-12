using Wec.Core.Contracts;
using Wec.Core.Microsoft365;
using Wec.Core.Objects;
using Wec.Core.Results;
using Wec.Modules.UserManagement.Domain;

namespace Wec.Modules.UserManagement.Application;

internal sealed class ScopedUserProfileService(IDirectoryUserSnapshotProvider directory,
    IMicrosoft365UserContextProvider cloud, UserManagementService users, WecWorkspaceIdentity workspace)
{
    internal async Task<Result<ScopedUserProfile>> GetAsync(ScopedUserProfileRequest request, CancellationToken cancellationToken)
    {
        ObjectReference reference = request.Reference;
        if (reference is null || reference.Kind != ObjectKind.User || reference.Source is not (ObjectSource.ActiveDirectory or ObjectSource.Entra)
            || !Guid.TryParse(reference.Id, out Guid objectId) || objectId == Guid.Empty || string.IsNullOrWhiteSpace(reference.Scope)
            || reference.Scope.Length > 253 || reference.Scope.Any(char.IsControl)
            || reference.Source == ObjectSource.Entra && (!Guid.TryParse(reference.Scope, out Guid tenant) || tenant == Guid.Empty)
            || reference.Source == ObjectSource.ActiveDirectory && Uri.CheckHostName(reference.Scope.Trim().TrimEnd('.')) != UriHostNameType.Dns)
        {
            return Invalid("Use a valid directory- or tenant-scoped user reference.");
        }
        reference = reference with { Id = objectId.ToString("D"), Scope = reference.Source == ObjectSource.Entra
            ? Guid.Parse(reference.Scope).ToString("D") : reference.Scope.Trim().TrimEnd('.').ToLowerInvariant() };
        string? directoryScope = reference.Source == ObjectSource.ActiveDirectory ? reference.Scope
            : (request.DirectoryScope ?? request.Connection?.Domain)?.Trim().TrimEnd('.').ToLowerInvariant();
        if (directoryScope is not null && (directoryScope.Length > 253 || Uri.CheckHostName(directoryScope) != UriHostNameType.Dns))
        {
            return Invalid("The selected AD scope must be the directory DNS name.");
        }
        Result<DirectoryUserReadConnection> connection = (request.Connection ?? new())
            .ToConnection();
        if (connection.IsFailure) { return Result.Failure<ScopedUserProfile>(connection.Error!); }
        List<Error> errors = [];
        CachedDirectoryUser? ad = null;
        DirectoryUserLookup? adQuery = null;
        if (reference.Source == ObjectSource.ActiveDirectory)
        {
            adQuery = new(connection.Value with { Domain = connection.Value.Domain ?? directoryScope }, directoryScope!, objectId);
            ad = await ReadDirectoryAsync(adQuery, request.LoadDirectoryIdentity, errors, cancellationToken);
        }
        string? sid = Microsoft365QueryValidation.AccountSid(ad?.Data?.User?.Sid);
        Result<Microsoft365UserContext> initial = await cloud.ReadCachedAsync(reference.Source == ObjectSource.Entra ? reference.Scope : null,
            reference.Source == ObjectSource.Entra ? reference.Id : null, sid, cancellationToken);
        if (initial.IsFailure && reference.Source == ObjectSource.Entra) { return Result.Failure<ScopedUserProfile>(initial.Error!); }
        if (initial.IsFailure) { errors.Add(initial.Error!); }
        Microsoft365UserContext? context = initial.IsSuccess ? initial.Value : null;
        Microsoft365User? primary = null;
        Microsoft365User[] sourceUsers = reference.Source == ObjectSource.Entra && context is not null
            ? UserAccountRelationships.Primary(reference.Id, context) : [];
        if (sourceUsers.Length == 1) { primary = sourceUsers[0]; }
        if (reference.Source == ObjectSource.Entra && primary is not null)
        {
            sid = Microsoft365QueryValidation.AccountSid(primary.OnPremisesSid);
            if (sid is not null && directoryScope is not null)
            {
                adQuery = new(connection.Value with { Domain = connection.Value.Domain ?? directoryScope }, directoryScope, SecurityIdentifier: sid);
                ad = await ReadDirectoryAsync(adQuery, request.LoadDirectoryIdentity, errors, cancellationToken);
            }
            else if (request.LoadDirectoryIdentity)
            {
                errors.Add(new(ErrorCode.InvalidRequest, "An exact synchronized account SID and an explicit AD directory scope are required for inverse resolution."));
            }
        }
        IReadOnlyList<ObjectRelationship> accounts = context is null ? [] : UserAccountRelationships.Candidates(ad?.Data?.User, context);
        ObjectRelationship[] confirmedAccounts = accounts.Where(link => link.Evidence == IdentityEvidence.ScopedId).ToArray();
        if (reference.Source == ObjectSource.ActiveDirectory && confirmedAccounts.Select(link => link.Target).Distinct().Count() == 1)
        {
            Microsoft365User[] matches = UserAccountRelationships.Primary(confirmedAccounts[0].Target.Id, context!);
            if (matches.Length == 1) { primary = matches[0]; }
        }
        if (context is not null && (primary is not null || sid is not null))
        {
            Result<Microsoft365UserContext> final = await cloud.ReadCachedAsync(context.TenantId, primary?.Id, sid, cancellationToken);
            if (final.IsFailure || final.Value.SessionRevision != context.SessionRevision)
            {
                return Result.Failure<ScopedUserProfile>(final.Error ?? new(ErrorCode.Microsoft365NotConnected, "The Microsoft 365 session changed during composition. Reopen this profile."));
            }
            context = final.Value;
            if (primary is not null)
            {
                Microsoft365User[] current = UserAccountRelationships.Primary(primary.Id!, context);
                primary = current.Length == 1 ? current[0] : null;
            }
            accounts = UserAccountRelationships.Candidates(ad?.Data?.User, context);
        }
        bool joined = primary is not null && ad?.Data?.User is { } account && context is not null
            && Microsoft365QueryValidation.AccountSid(account.Sid) is { } accountSid
            && Microsoft365QueryValidation.AccountSid(primary.OnPremisesSid) == accountSid
            && UserAccountRelationships.HasUniqueSid(context, accountSid, primary.Id);
        if (reference.Source == ObjectSource.ActiveDirectory && !joined) { primary = null; }
        if (primary is null && context is not null)
        {
            context = context with { UserLicenses = null, DirectGroups = null, RegisteredDevices = null, AssociatedIntune = [], SignIn = null, Registration = null };
        }
        UserProfileResult? adProfile = null;
        if (ad?.Data?.User is { } directoryUser && (reference.Source == ObjectSource.ActiveDirectory || joined))
        {
            Result<UserProfileResult> composed = await users.ComposeAsync(directoryUser, cancellationToken);
            if (composed.IsSuccess) { adProfile = composed.Value; } else { errors.Add(composed.Error!); }
        }
        if (adQuery is not null && ad is not null)
        {
            CachedDirectoryUser? current = await directory.ReadCachedAsync(adQuery, cancellationToken);
            if (current?.SessionRevision != ad.SessionRevision || current.Revision != ad.Revision)
            {
                return Invalid("The AD snapshot changed during composition. Reopen this profile.");
            }
        }
        if (context is not null)
        {
            Result<Microsoft365UserContext> current = await cloud.ReadCachedAsync(context.TenantId, primary?.Id, sid, cancellationToken);
            if (current.IsFailure || current.Value.SessionRevision != context.SessionRevision || current.Value.Revision != context.Revision)
            {
                return Result.Failure<ScopedUserProfile>(current.Error ?? new(ErrorCode.Microsoft365NotConnected, "The Microsoft 365 working set changed during composition. Reopen this profile."));
            }
        }
        List<ObjectRelationship> links = [];
        List<ObjectRelationship> candidates = accounts.Where(link => link.Evidence != IdentityEvidence.ScopedId).ToList();
        if (joined)
        {
            links.Add(reference.Source == ObjectSource.ActiveDirectory ? accounts.First(link => link.Evidence == IdentityEvidence.ScopedId)
                : new(new(ObjectKind.User, ObjectSource.ActiveDirectory, directoryScope!, ad!.Data!.User!.ObjectId.ToString("D")), ad.Data.User.DisplayName,
                    "AD account", IdentityEvidence.ScopedId, "Exact synchronized SID, bounded unique AD result and unique Entra SID evidence in the selected scopes."));
        }
        else if (reference.Source == ObjectSource.Entra && ad?.Data?.User is { } possible)
        {
            candidates.Add(new(new(ObjectKind.User, ObjectSource.ActiveDirectory, directoryScope!, possible.ObjectId.ToString("D")), possible.DisplayName,
                "Possible AD account", IdentityEvidence.Ambiguous, "The AD SID resolves, but uniqueness or consistency of the Entra SID is not established. No AD facts are composed into this account."));
        }
        if (primary is not null && context is not null)
        {
            IReadOnlyList<ObjectRelationship> cloudLinks = UserAccountRelationships.CloudLinks(context, primary.Id);
            links.AddRange(cloudLinks.Where(link => link.Evidence == IdentityEvidence.ScopedId));
            candidates.AddRange(cloudLinks.Where(link => link.Evidence != IdentityEvidence.ScopedId));
        }
        foreach (UserLinkedDeviceProfile device in adProfile?.Devices.LinkedDevices ?? [])
        {
            links.Add(new(new(ObjectKind.Device, ObjectSource.Wec, workspace.Scope, device.Host), device.Host, "Stored SID observation (Windows)",
                IdentityEvidence.ScopedId, "Stored Inventory evidence matches this AD account SID. It does not establish ownership or current assignment."));
        }
        IdentityEvidence identity = reference.Source == ObjectSource.ActiveDirectory ? adProfile is null ? IdentityEvidence.Unresolved : IdentityEvidence.ScopedId
            : sourceUsers.Length > 1 ? IdentityEvidence.Ambiguous : primary is null ? IdentityEvidence.Unresolved : IdentityEvidence.ScopedId;
        string title = reference.Source == ObjectSource.ActiveDirectory ? adProfile?.Identity.DisplayName ?? reference.Id
            : primary?.DisplayName ?? primary?.UserPrincipalName ?? reference.Id;
        cancellationToken.ThrowIfCancellationRequested();
        return Result.Success(new ScopedUserProfile(reference, title, identity,
            "This is one source-scoped account. AD remains authoritative for its own account. Other accounts and unresolved candidates remain separate; missing evidence does not prove absence.",
            ad, adProfile, context is null ? null : UserAccountRelationships.Filter(context, primary, accounts), primary,
            links.Distinct().ToArray(), candidates.Where(link => link.Target != reference).Distinct().ToArray(), errors));
    }

    private async Task<CachedDirectoryUser?> ReadDirectoryAsync(DirectoryUserLookup query, bool refresh, List<Error> errors,
        CancellationToken cancellationToken)
    {
        if (refresh)
        {
            Result<DirectoryUserIdentityResult> result = await directory.ReadIdentityAsync(query, cancellationToken);
            if (result.IsFailure) { errors.Add(result.Error!); }
        }
        return await directory.ReadCachedAsync(query, cancellationToken);
    }

    private static Result<ScopedUserProfile> Invalid(string message) => Result.Failure<ScopedUserProfile>(new(ErrorCode.InvalidRequest, message));
}
