using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Results;

namespace Wec.Modules.ActiveDirectory.Application;

internal sealed class DirectoryUserReadService : IDirectoryUserReadProvider
{
    private readonly DomainContextService _domainContextService;
    private readonly IDirectoryReader _directoryReader;
    private readonly PrivilegedGroupResolver _privilegedGroupResolver;
    private readonly ActiveDirectoryOptions _options;

    public DirectoryUserReadService(
        DomainContextService domainContextService,
        IDirectoryReader directoryReader,
        PrivilegedGroupResolver privilegedGroupResolver,
        IOptions<ActiveDirectoryOptions> options)
    {
        _domainContextService = domainContextService;
        _directoryReader = directoryReader;
        _privilegedGroupResolver = privilegedGroupResolver;
        _options = options.Value;
    }

    public async Task<Result<DirectoryUserPage>> GetPageAsync(
        DirectoryUserPageQuery query,
        CancellationToken cancellationToken)
    {
        Result<int> offset = ValidatePageQuery(query);
        if (offset.IsFailure)
        {
            return Result.Failure<DirectoryUserPage>(offset.Error!);
        }

        DirectoryConnection connection = ToConnection(query.Connection);
        Result<DomainContext> context = await _domainContextService.GetContextAsync(connection, cancellationToken);
        if (context.IsFailure)
        {
            return Result.Failure<DirectoryUserPage>(context.Error!);
        }

        if (!context.Value.DomainJoined)
        {
            return Result.Success(new DirectoryUserPage(
                false, null, null, query.Page, query.PageSize, 0, []));
        }

        string baseDn = string.IsNullOrWhiteSpace(query.BaseDistinguishedName)
            ? context.Value.DefaultNamingContext!
            : query.BaseDistinguishedName.Trim();
        string sortAttribute = SortAttribute(query.SortField);
        Result<BoundedDirectorySearchResult> entries = await _directoryReader.SearchPageAsync(
            new DirectorySearchQuery(
                context.Value.DomainName!,
                baseDn,
                AdFilters.DirectoryUsers(query.Search, query.AccountState, query.Department),
                DirectoryUserMapper.Attributes,
                DirectorySearchScope.Subtree,
                _options.PageSize,
                _options.SearchTimeout,
                connection.Server,
                connection.Credentials,
                SortAttribute: sortAttribute,
                SortDescending: query.SortDirection == DirectoryUserSortDirection.Descending),
            offset.Value,
            query.PageSize,
            cancellationToken);
        if (entries.IsFailure)
        {
            return Result.Failure<DirectoryUserPage>(entries.Error!);
        }

        Result<IReadOnlyList<DirectoryUserRecord>> mapped = DirectoryUserMapper.Map(entries.Value.Entries);
        return mapped.IsFailure
            ? Result.Failure<DirectoryUserPage>(mapped.Error!)
            : Result.Success(new DirectoryUserPage(
                true,
                context.Value.DomainName,
                baseDn,
                query.Page,
                query.PageSize,
                entries.Value.TotalCount,
                mapped.Value));
    }

    public async Task<Result<DirectoryUserRecord?>> GetByIdAsync(
        DirectoryUserIdentityQuery query,
        CancellationToken cancellationToken)
    {
        DirectoryConnection connection = ToConnection(query.Connection);
        Result<DomainContext> context = await _domainContextService.GetContextAsync(connection, cancellationToken);
        if (context.IsFailure)
        {
            return Result.Failure<DirectoryUserRecord?>(context.Error!);
        }

        if (!context.Value.DomainJoined)
        {
            return Result.Success<DirectoryUserRecord?>(null);
        }

        Result<BoundedDirectorySearchResult> entries = await _directoryReader.SearchBoundedAsync(
            new DirectorySearchQuery(
                context.Value.DomainName!,
                context.Value.DefaultNamingContext!,
                AdFilters.UserByObjectGuid(query.ObjectId),
                DirectoryUserMapper.Attributes,
                DirectorySearchScope.Subtree,
                _options.PageSize,
                _options.SearchTimeout,
                connection.Server,
                connection.Credentials),
            entryLimit: 1,
            cancellationToken);
        if (entries.IsFailure)
        {
            return Result.Failure<DirectoryUserRecord?>(entries.Error!);
        }

        if (entries.Value.Entries.Count == 0)
        {
            return Result.Success<DirectoryUserRecord?>(null);
        }

        Result<IReadOnlyList<ResolvedPrivilegedGroup>> privilegedGroups =
            await _privilegedGroupResolver.ResolveAsync(
                context.Value.DomainName!,
                context.Value.DefaultNamingContext!,
                connection,
                cancellationToken);
        DirectoryUserPrivilegedAccess privilegedAccess = privilegedGroups.IsSuccess
            ? MapPrivilegedAccess(entries.Value.Entries[0], privilegedGroups.Value)
            : new DirectoryUserPrivilegedAccess(
                DirectoryUserAccessCoverage.Unavailable,
                "The SID-validated privileged-group allowlist could not be evaluated.",
                []);
        Result<DirectoryUserRecord> mapped = DirectoryUserMapper.Map(
            entries.Value.Entries[0], privilegedAccess);
        return mapped.IsFailure
            ? Result.Failure<DirectoryUserRecord?>(mapped.Error!)
            : Result.Success<DirectoryUserRecord?>(mapped.Value);
    }

    private static DirectoryUserPrivilegedAccess MapPrivilegedAccess(
        DirectoryEntryData user,
        IReadOnlyList<ResolvedPrivilegedGroup> privilegedGroups)
    {
        HashSet<string> directGroupDns = user.GetValues("memberOf")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<DirectoryUserGroup> memberships = [.. privilegedGroups
            .Where(group => directGroupDns.Contains(group.DistinguishedName))
            .Select(group => new DirectoryUserGroup(group.DistinguishedName, group.GroupName))
            .OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase)];
        return new DirectoryUserPrivilegedAccess(
            DirectoryUserAccessCoverage.Available,
            "Direct memberships were compared with the SID-validated privileged-group allowlist.",
            memberships);
    }

    private static DirectoryConnection ToConnection(DirectoryUserReadConnection connection) =>
        new(connection.Domain, connection.Server, connection.Credentials);

    private static Result<int> ValidatePageQuery(DirectoryUserPageQuery query)
    {
        if (query.Page < 1 || query.PageSize is < 1 or > 100)
        {
            return InvalidPage("Page must be at least 1 and pageSize must be between 1 and 100.");
        }

        if ((query.Search?.Trim().Length ?? 0) > 100)
        {
            return InvalidPage("The user search must not exceed 100 characters.");
        }

        if ((query.Department?.Trim().Length ?? 0) > 100)
        {
            return InvalidPage("The department filter must not exceed 100 characters.");
        }

        if ((query.BaseDistinguishedName?.Trim().Length ?? 0) > 2_048)
        {
            return InvalidPage("The base distinguished name must not exceed 2048 characters.");
        }

        if (!Enum.IsDefined(query.AccountState)
            || !Enum.IsDefined(query.SortField)
            || !Enum.IsDefined(query.SortDirection))
        {
            return InvalidPage("The user filter or sort value is not supported.");
        }

        long offset = ((long)query.Page - 1) * query.PageSize;
        return offset > int.MaxValue
            ? InvalidPage("The requested user page is too large.")
            : Result.Success((int)offset);
    }

    private static Result<int> InvalidPage(string message) =>
        Result.Failure<int>(new Error(ErrorCode.InvalidRequest, message));

    private static string SortAttribute(DirectoryUserSortField sortField) => sortField switch
    {
        DirectoryUserSortField.SamAccountName => "sAMAccountName",
        DirectoryUserSortField.Department => "department",
        DirectoryUserSortField.CreatedAt => "whenCreated",
        DirectoryUserSortField.LastLogon => "lastLogonTimestamp",
        _ => "displayName",
    };
}
