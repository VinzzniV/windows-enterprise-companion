using Wec.Core.Contracts;
using Wec.Core.Microsoft365;
using Wec.Core.Objects;
using Wec.Core.Results;
using Wec.Core.Targets;

namespace Wec.Modules.GroupManagement;

internal sealed class GroupProfileService(IDirectoryGroupReadProvider directory, IMicrosoft365GroupContextProvider cloud)
{
    internal async Task<Result<GroupProfileResult>> GetAsync(GroupProfileRequest request, CancellationToken cancellationToken)
    {
        ObjectReference reference = request.Reference;
        if (reference is null || reference.Kind != ObjectKind.Group || reference.Source is not (ObjectSource.ActiveDirectory or ObjectSource.Entra)
            || !ValidId(reference.Id) || !Enum.IsDefined(request.Read) || !ValidPage(request.MemberPage, request.MemberPageSize)
            || (reference.Source == ObjectSource.ActiveDirectory ? !ValidScope(reference.Scope) : !ValidId(reference.Scope)))
        {
            return Invalid<GroupProfileResult>("Use a scoped AD or Entra group GUID and a bounded member page.");
        }
        reference = reference with { Id = Guid.Parse(reference.Id).ToString("D"), Scope = reference.Source == ObjectSource.Entra
            ? Guid.Parse(reference.Scope).ToString("D") : NormalizeScope(reference.Scope) };
        CachedDirectoryGroupIdentity? ad = null;
        CachedDirectoryGroupMembers? members = null;
        Microsoft365GroupContext? context = null;
        List<Error> errors = [];
        List<ObjectRelationship> links = [];
        string title = reference.Id;
        IdentityEvidence identity = IdentityEvidence.Unresolved;
        if (reference.Source == ObjectSource.ActiveDirectory)
        {
            Result<DirectoryUserReadConnection> connection = Connection(request.Connection, reference.Scope);
            if (connection.IsFailure) { return Result.Failure<GroupProfileResult>(connection.Error!); }
            var query = new DirectoryGroupIdentityQuery(connection.Value, reference.Scope, Guid.Parse(reference.Id));
            var memberQuery = new DirectoryGroupMemberQuery(connection.Value, reference.Scope, Guid.Parse(reference.Id), request.MemberPage, request.MemberPageSize);
            if (request.Read == GroupProfileRead.DirectoryIdentity)
            {
                var loaded = await directory.ReadIdentityAsync(query, cancellationToken);
                if (loaded.IsFailure) { errors.Add(loaded.Error!); }
            }
            if (request.Read == GroupProfileRead.DirectoryMembers)
            {
                var loaded = await directory.ReadMembersAsync(memberQuery, cancellationToken);
                if (loaded.IsFailure) { errors.Add(loaded.Error!); }
            }
            ad = await directory.ReadCachedIdentityAsync(query, cancellationToken);
            members = await directory.ReadCachedMembersAsync(memberQuery, cancellationToken);
            if (ad.State.SessionRevision != members.State.SessionRevision) { return Invalid<GroupProfileResult>("The directory context changed while composing this group."); }
            if (ad.Data is not null && NormalizeScope(ad.Data.DirectoryScope) != reference.Scope
                || members.Data is not null && (NormalizeScope(members.Data.DirectoryScope) != reference.Scope || members.Data.GroupObjectId != query.ObjectId))
            {
                return Invalid<GroupProfileResult>("Cached directory facts do not belong to this scoped group.");
            }
            DirectoryGroupRecord[] groups = ad.Data?.Groups.Where(group => group.ObjectId == query.ObjectId).ToArray() ?? [];
            identity = ad.Data?.Truncated == true || groups.Length > 1 ? IdentityEvidence.Ambiguous : groups.Length == 1 ? IdentityEvidence.ScopedId : IdentityEvidence.Unresolved;
            if (groups.Length == 1) { title = groups[0].Name; }
            if (identity == IdentityEvidence.ScopedId)
            {
                foreach (DirectoryGroupMember member in members.Data?.Members ?? [])
                {
                    if (member.ObjectId is { } id && id != Guid.Empty && member.Kind is { } kind)
                    {
                        links.Add(new(new(kind, ObjectSource.ActiveDirectory, reference.Scope, id.ToString("D")), member.DisplayName,
                            "Direct member (AD)", IdentityEvidence.ScopedId, "Native GUID and type returned by this direct memberOf query. Nested groups open separately; no recursive expansion."));
                    }
                }
            }
        }
        else
        {
            if (request.Read != GroupProfileRead.Cached) { return Invalid<GroupProfileResult>("An Entra group cannot trigger an AD source read."); }
            Result<Microsoft365GroupContext> read = await cloud.ReadGroupCachedAsync(reference.Scope, reference.Id, cancellationToken);
            if (read.IsFailure) { return Result.Failure<GroupProfileResult>(read.Error!); }
            context = read.Value;
            if (!SameId(context.TenantId, reference.Scope)) { return Invalid<GroupProfileResult>("The cached group belongs to another tenant."); }
            CachedMicrosoft365Groups? detail = context.GroupReads.FirstOrDefault(source => source.State.Query.Resource == Microsoft365Resource.Group
                && source.State.Availability == Microsoft365Availability.Available && SameId(source.State.Query.ObjectId, reference.Id));
            Microsoft365Group[] groups = (detail?.Groups ?? context.GroupReads.Where(source => source.State.Query.Resource == Microsoft365Resource.Groups)
                .SelectMany(source => source.Groups)).Where(group => SameId(group.Id, reference.Id)).ToArray();
            identity = groups.Length > 1 ? IdentityEvidence.Ambiguous : groups.Length == 1 ? IdentityEvidence.ScopedId : IdentityEvidence.Unresolved;
            if (groups.Length == 1) { title = groups[0].DisplayName ?? reference.Id; }
            if (identity == IdentityEvidence.ScopedId)
            {
                foreach (Microsoft365Member member in context.DirectMembers?.Members ?? [])
                {
                    ObjectKind? kind = member.ObjectType switch
                    {
                        "user" or "#microsoft.graph.user" => ObjectKind.User,
                        "group" or "#microsoft.graph.group" => ObjectKind.Group,
                        "device" or "#microsoft.graph.device" => ObjectKind.Device,
                        _ => null,
                    };
                    if (kind is { } memberKind && ValidId(member.Id))
                    {
                        links.Add(new(new(memberKind, ObjectSource.Entra, reference.Scope, Guid.Parse(member.Id!).ToString("D")), member.DisplayName ?? member.Id!,
                            "Direct member (Entra)", IdentityEvidence.ScopedId, "Explicit source-native member ID/type. Limited fields remain unknown; membership does not establish effective access."));
                    }
                }
            }
            context = context with { GroupReads = context.GroupReads.Select(source => source with { Groups = source.Groups.Where(group => SameId(group.Id, reference.Id)).ToArray() }).ToArray() };
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Result.Success(new GroupProfileResult(reference, title, identity,
            "This group is anchored to its source and scope. AD and Entra groups remain separate. Direct membership is bounded source evidence, not a complete permissions analysis.",
            ad, members, context, links, errors));
    }

    internal async Task<Result<ObjectReference>> ResolveAsync(ResolveGroupRequest request, CancellationToken cancellationToken)
    {
        if (!ValidScope(request.DirectoryScope)) { return Invalid<ObjectReference>("A directory DNS scope is required."); }
        string scope = NormalizeScope(request.DirectoryScope);
        Result<DirectoryUserReadConnection> connection = Connection(request.Connection, scope);
        if (connection.IsFailure) { return Result.Failure<ObjectReference>(connection.Error!); }
        var result = await directory.ReadIdentityAsync(new(connection.Value, scope, DistinguishedName: request.DistinguishedName, SecurityIdentifier: request.SecurityIdentifier), cancellationToken);
        if (result.IsFailure) { return Result.Failure<ObjectReference>(result.Error!); }
        if (result.Value.Truncated || result.Value.Groups.Count != 1 || result.Value.Groups[0].ObjectId is not { } id || id == Guid.Empty
            || NormalizeScope(result.Value.Groups[0].DirectoryScope) != scope)
        {
            return Invalid<ObjectReference>("This bounded query did not establish one unique group GUID. No group was selected.");
        }
        return Result.Success(new ObjectReference(ObjectKind.Group, ObjectSource.ActiveDirectory, scope, id.ToString("D")));
    }

    internal async Task<Result<CachedDirectoryGroupPage>> PageAsync(DirectoryGroupPageRequest request, CancellationToken cancellationToken)
    {
        if (!ValidScope(request.DirectoryScope) || !ValidPage(request.Page, request.PageSize) || request.Search?.Length > 100)
        {
            return Invalid<CachedDirectoryGroupPage>("Use a directory DNS scope and a group page within the supported bounds.");
        }
        string scope = NormalizeScope(request.DirectoryScope);
        Result<DirectoryUserReadConnection> connection = Connection(request.Connection, scope);
        if (connection.IsFailure) { return Result.Failure<CachedDirectoryGroupPage>(connection.Error!); }
        var query = new DirectoryGroupPageQuery(connection.Value, scope, request.Search, request.Page, request.PageSize);
        if (request.Refresh) { await directory.ReadPageAsync(query, cancellationToken); }
        return Result.Success(await directory.ReadCachedPageAsync(query, cancellationToken));
    }

    private static Result<DirectoryUserReadConnection> Connection(DirectoryInventoryConnection? value, string scope)
    {
        ScanCredentials credentials = ScanCredentials.CurrentUser;
        if (!string.IsNullOrWhiteSpace(value?.UserName))
        {
            if (value.Password is null) { return Invalid<DirectoryUserReadConnection>("Explicit directory credentials require a password."); }
            Result<ScanCredentials> normalized = DirectoryScanCredentials.NormalizeExplicit(value.UserName, value.UserDomain, value.Password, value.Domain ?? scope);
            if (normalized.IsFailure) { return Result.Failure<DirectoryUserReadConnection>(normalized.Error!); }
            credentials = normalized.Value;
        }
        return Result.Success(new DirectoryUserReadConnection(string.IsNullOrWhiteSpace(value?.Domain) ? scope : value.Domain.Trim(),
            string.IsNullOrWhiteSpace(value?.Server) ? null : value.Server.Trim(), credentials));
    }
    private static bool ValidPage(int page, int size) => page >= 1 && size is >= 1 and <= 100 && (long)(page - 1) * size <= int.MaxValue;
    private static bool ValidId(string? value) => Guid.TryParse(value, out Guid id) && id != Guid.Empty;
    private static bool SameId(string? a, string? b) => ValidId(a) && ValidId(b) && Guid.Parse(a!) == Guid.Parse(b!);
    private static bool ValidScope(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 253 && Uri.CheckHostName(NormalizeScope(value)) == UriHostNameType.Dns;
    private static string NormalizeScope(string value) => value.Trim().TrimEnd('.').ToLowerInvariant();
    private static Result<T> Invalid<T>(string message) => Result.Failure<T>(new(ErrorCode.InvalidRequest, message));
}
