using System.Globalization;
using System.Security.Principal;
using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Objects;
using Wec.Core.Results;

namespace Wec.Modules.ActiveDirectory.Application;

internal sealed class DirectoryGroupReadService(DomainContextService domainContext, IDirectoryReader reader,
    IClock clock, IOptions<ActiveDirectoryOptions> options, DirectoryGroupSnapshotCache cache) : IDirectoryGroupReadProvider
{
    private static readonly string[] GroupAttributes = ["objectGUID", "objectSid", "name", "sAMAccountName", "description", "groupType", "distinguishedName"];
    private static readonly string[] MemberAttributes = ["objectGUID", "objectSid", "objectClass", "displayName", "name", "sAMAccountName", "userPrincipalName", "distinguishedName"];

    public Task<CachedDirectoryGroupIdentity> ReadCachedIdentityAsync(DirectoryGroupIdentityQuery query, CancellationToken cancellationToken)
    {
        var read = cache.Read(query.Connection, query.DirectoryScope, DirectoryGroupSnapshotCache.IdentityKey(query), cancellationToken, query.ObjectId);
        return Task.FromResult(new CachedDirectoryGroupIdentity(read.State, read.Data.Identity));
    }
    public Task<CachedDirectoryGroupMembers> ReadCachedMembersAsync(DirectoryGroupMemberQuery query, CancellationToken cancellationToken)
    {
        var read = cache.Read(query.Connection, query.DirectoryScope, DirectoryGroupSnapshotCache.MembersKey(query), cancellationToken);
        return Task.FromResult(new CachedDirectoryGroupMembers(read.State, read.Data.Members));
    }
    public Task<CachedDirectoryGroupPage> ReadCachedPageAsync(DirectoryGroupPageQuery query, CancellationToken cancellationToken)
    {
        var read = cache.Read(query.Connection, query.DirectoryScope, DirectoryGroupSnapshotCache.PageKey(query), cancellationToken);
        return Task.FromResult(new CachedDirectoryGroupPage(read.State, read.Data.Page));
    }
    public async Task<Result<DirectoryGroupIdentityResult>> ReadIdentityAsync(DirectoryGroupIdentityQuery query, CancellationToken cancellationToken)
    {
        if (!ValidIdentity(query)) { return Invalid<DirectoryGroupIdentityResult>("A directory scope and exactly one valid group identity are required."); }
        var result = await cache.LoadAsync(query.Connection, query.DirectoryScope, DirectoryGroupSnapshotCache.IdentityKey(query), async token =>
        {
            var loaded = await LoadIdentityAsync(query, token);
            return loaded.IsSuccess ? Result.Success(new DirectoryGroupReadData(Identity: loaded.Value)) : Result.Failure<DirectoryGroupReadData>(loaded.Error!);
        }, cancellationToken);
        return result.IsSuccess ? Result.Success(result.Value.Identity!) : Result.Failure<DirectoryGroupIdentityResult>(result.Error!);
    }
    public async Task<Result<DirectoryGroupPage>> ReadPageAsync(DirectoryGroupPageQuery query, CancellationToken cancellationToken)
    {
        if (!ValidScope(query.DirectoryScope) || !ValidPage(query.Page, query.PageSize) || query.Search?.Length > 100)
        {
            return Invalid<DirectoryGroupPage>("The requested group page is outside the supported bounds.");
        }
        var result = await cache.LoadAsync(query.Connection, query.DirectoryScope, DirectoryGroupSnapshotCache.PageKey(query), async token =>
        {
            var loaded = await LoadPageAsync(query, token);
            return loaded.IsSuccess ? Result.Success(new DirectoryGroupReadData(Page: loaded.Value)) : Result.Failure<DirectoryGroupReadData>(loaded.Error!);
        }, cancellationToken);
        return result.IsSuccess ? Result.Success(result.Value.Page!) : Result.Failure<DirectoryGroupPage>(result.Error!);
    }
    public async Task<Result<DirectoryGroupMemberPage>> ReadMembersAsync(DirectoryGroupMemberQuery query, CancellationToken cancellationToken)
    {
        if (!ValidScope(query.DirectoryScope) || !ValidPage(query.Page, query.PageSize) || query.GroupObjectId == Guid.Empty)
        {
            return Invalid<DirectoryGroupMemberPage>("The requested member page or group identity is invalid.");
        }
        var result = await cache.LoadAsync(query.Connection, query.DirectoryScope, DirectoryGroupSnapshotCache.MembersKey(query), async token =>
        {
            var loaded = await LoadMembersAsync(query, token);
            return loaded.IsSuccess ? Result.Success(new DirectoryGroupReadData(Members: loaded.Value)) : Result.Failure<DirectoryGroupReadData>(loaded.Error!);
        }, cancellationToken);
        return result.IsSuccess ? Result.Success(result.Value.Members!) : Result.Failure<DirectoryGroupMemberPage>(result.Error!);
    }

    private async Task<Result<DirectoryGroupIdentityResult>> LoadIdentityAsync(DirectoryGroupIdentityQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? sid = NormalizeSid(query.SecurityIdentifier);
        if (!ValidIdentity(query))
        {
            return Invalid<DirectoryGroupIdentityResult>("Specify a directory DNS scope and exactly one valid group GUID, SID or distinguished name.");
        }
        Result<DomainContext> context = await ContextAsync(query.Connection, query.DirectoryScope, cancellationToken);
        if (context.IsFailure) { return Result.Failure<DirectoryGroupIdentityResult>(context.Error!); }
        string filter = query.ObjectId is { } id ? AdFilters.GroupByObjectGuid(id)
            : sid is not null ? AdFilters.GroupBySid(sid) : AdFilters.GroupByDistinguishedName(query.DistinguishedName!);
        Result<BoundedDirectorySearchResult> result = await reader.SearchBoundedAsync(Search(query.Connection, context.Value, filter, GroupAttributes), 2, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (result.IsFailure) { return Result.Failure<DirectoryGroupIdentityResult>(result.Error!); }
        string scope = DirectoryIdentityValues.DirectoryScope(context.Value.DefaultNamingContext!)!;
        DirectoryGroupRecord[] groups = result.Value.Entries.Select(entry => MapGroup(entry, scope)).ToArray();
        if (groups.Any(group => query.ObjectId is { } guid ? group.ObjectId != guid : sid is not null
            ? group.SecurityIdentifier != sid : !string.Equals(group.DistinguishedName, query.DistinguishedName, StringComparison.OrdinalIgnoreCase)))
        {
            return Invalid<DirectoryGroupIdentityResult>("The directory response did not contain the requested exact group identity.");
        }
        return Result.Success(new DirectoryGroupIdentityResult(scope, clock.UtcNow, groups, result.Value.TotalCount > groups.Length));
    }

    private async Task<Result<DirectoryGroupPage>> LoadPageAsync(DirectoryGroupPageQuery query, CancellationToken cancellationToken)
    {
        if (!ValidScope(query.DirectoryScope) || !ValidPage(query.Page, query.PageSize) || query.Search?.Length > 100)
        {
            return Invalid<DirectoryGroupPage>("Use a directory DNS scope, a search of at most 100 characters and a page size from 1 to 100.");
        }
        Result<DomainContext> context = await ContextAsync(query.Connection, query.DirectoryScope, cancellationToken);
        if (context.IsFailure) { return Result.Failure<DirectoryGroupPage>(context.Error!); }
        Result<BoundedDirectorySearchResult> result = await reader.SearchPageAsync(Search(query.Connection, context.Value,
            AdFilters.WithAccountNameSearch(AdFilters.Groups, query.Search), GroupAttributes) with
            { SortAttribute = "name", SortTieBreakerAttribute = "sAMAccountName" },
            (query.Page - 1) * query.PageSize, query.PageSize, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (result.IsFailure) { return Result.Failure<DirectoryGroupPage>(result.Error!); }
        string scope = DirectoryIdentityValues.DirectoryScope(context.Value.DefaultNamingContext!)!;
        return Result.Success(new DirectoryGroupPage(scope, clock.UtcNow, query.Page, query.PageSize, result.Value.TotalCount,
            result.Value.Entries.Select(entry => MapGroup(entry, scope)).ToArray()));
    }

    private async Task<Result<DirectoryGroupMemberPage>> LoadMembersAsync(DirectoryGroupMemberQuery query, CancellationToken cancellationToken)
    {
        if (!ValidPage(query.Page, query.PageSize) || query.GroupObjectId == Guid.Empty)
        {
            return Invalid<DirectoryGroupMemberPage>("Use a non-empty group GUID and a page size from 1 to 100.");
        }
        Result<DirectoryGroupIdentityResult> identity = await LoadIdentityAsync(new(query.Connection, query.DirectoryScope, query.GroupObjectId), cancellationToken);
        if (identity.IsFailure) { return Result.Failure<DirectoryGroupMemberPage>(identity.Error!); }
        if (identity.Value.Truncated || identity.Value.Groups.Count != 1 || identity.Value.Groups[0].ObjectId != query.GroupObjectId)
        {
            return Invalid<DirectoryGroupMemberPage>("A unique scoped group identity is required before reading members.");
        }
        DirectoryGroupRecord group = identity.Value.Groups[0];
        if (!ValidDn(group.DistinguishedName)) { return Invalid<DirectoryGroupMemberPage>("The group has no usable distinguished name for a direct-membership read."); }
        Result<DomainContext> context = await ContextAsync(query.Connection, group.DirectoryScope, cancellationToken);
        if (context.IsFailure) { return Result.Failure<DirectoryGroupMemberPage>(context.Error!); }
        Result<BoundedDirectorySearchResult> result = await reader.SearchPageAsync(Search(query.Connection, context.Value,
            AdFilters.DirectMembersOfGroup(group.DistinguishedName), MemberAttributes) with
            { SortAttribute = "name", SortTieBreakerAttribute = "sAMAccountName" },
            (query.Page - 1) * query.PageSize, query.PageSize, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (result.IsFailure) { return Result.Failure<DirectoryGroupMemberPage>(result.Error!); }
        return Result.Success(new DirectoryGroupMemberPage(group.DirectoryScope, query.GroupObjectId, clock.UtcNow,
            query.Page, query.PageSize, result.Value.TotalCount, result.Value.Entries.Select(MapMember).ToArray(),
            "Direct memberOf matches visible in this directory naming context only. Primary-group membership, nested expansion, foreign-directory coverage and effective permissions are not evaluated. Counts describe this source query."));
    }

    private async Task<Result<DomainContext>> ContextAsync(DirectoryUserReadConnection connection, string expectedScope, CancellationToken cancellationToken)
    {
        Result<DomainContext> result = await domainContext.GetContextAsync(new(connection.Domain ?? expectedScope, connection.Server, connection.Credentials), cancellationToken);
        if (result.IsFailure) { return result; }
        string? actual = result.Value.DefaultNamingContext is { } dn ? DirectoryIdentityValues.DirectoryScope(dn) : null;
        return actual is not null && string.Equals(actual, expectedScope.Trim().TrimEnd('.'), StringComparison.OrdinalIgnoreCase)
            ? result : Invalid<DomainContext>("The connected directory naming context does not match the group scope.");
    }

    private DirectorySearchQuery Search(DirectoryUserReadConnection connection, DomainContext context, string filter, IReadOnlyList<string> attributes) =>
        new(context.DomainName!, context.DefaultNamingContext!, filter, attributes, DirectorySearchScope.Subtree,
            options.Value.PageSize, options.Value.SearchTimeout, connection.Server, connection.Credentials);

    internal static DirectoryGroupRecord MapGroup(DirectoryEntryData entry, string scope)
    {
        uint? type = long.TryParse(entry.GetFirstValue("groupType"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
            && value is >= int.MinValue and <= uint.MaxValue ? unchecked((uint)value) : null;
        string? groupScope = type is null ? null : (type.Value & 0xE) switch { 2 => "Global", 4 => "DomainLocal", 8 => "Universal", _ => null };
        return new(DirectoryIdentityValues.ObjectId(entry), DirectoryIdentityValues.SecurityIdentifier(entry), scope,
            entry.GetFirstValue("name") ?? entry.DistinguishedName, entry.GetFirstValue("sAMAccountName"), entry.DistinguishedName,
            entry.GetFirstValue("description"), type is null ? null : (type.Value & 0x80000000) != 0, groupScope);
    }

    internal static DirectoryGroupMember MapMember(DirectoryEntryData entry)
    {
        string[] classes = entry.GetValues("objectClass").ToArray();
        ObjectKind? kind = classes.Contains("computer", StringComparer.OrdinalIgnoreCase) ? ObjectKind.Device
            : classes.Contains("group", StringComparer.OrdinalIgnoreCase) ? ObjectKind.Group
            : classes.Contains("user", StringComparer.OrdinalIgnoreCase) ? ObjectKind.User : null;
        return new(DirectoryIdentityValues.ObjectId(entry), DirectoryIdentityValues.SecurityIdentifier(entry), kind,
            classes.LastOrDefault(), entry.GetFirstValue("displayName") ?? entry.GetFirstValue("name") ?? entry.DistinguishedName,
            entry.GetFirstValue("sAMAccountName"), kind == ObjectKind.User ? entry.GetFirstValue("userPrincipalName") : null, entry.DistinguishedName);
    }

    private static bool ValidScope(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 253
        && Uri.CheckHostName(value.Trim().TrimEnd('.')) == UriHostNameType.Dns;
    private static bool ValidIdentity(DirectoryGroupIdentityQuery query) => ValidScope(query.DirectoryScope) && query.ObjectId != Guid.Empty
        && (query.ObjectId is null ? 0 : 1) + (query.SecurityIdentifier is null ? 0 : 1) + (query.DistinguishedName is null ? 0 : 1) == 1
        && (query.SecurityIdentifier is null || NormalizeSid(query.SecurityIdentifier) is not null)
        && (query.DistinguishedName is null || ValidDn(query.DistinguishedName));
    private static bool ValidPage(int page, int size) => page >= 1 && size is >= 1 and <= 100 && (long)(page - 1) * size <= int.MaxValue;
    private static bool ValidDn(string value) => value.Length is > 0 and <= 4096 && !value.Any(char.IsControl) && value.Contains('=', StringComparison.Ordinal);
    private static string? NormalizeSid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 184) { return null; }
        try { return new SecurityIdentifier(value).Value; }
        catch (ArgumentException) { return null; }
    }
    private static Result<T> Invalid<T>(string message) => Result.Failure<T>(new(ErrorCode.InvalidRequest, message));
}
