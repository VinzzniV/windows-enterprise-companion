using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Results;

namespace Wec.Modules.ActiveDirectory.Application;

public sealed record AdUser(
    string Name,
    string? SamAccountName,
    string? UserPrincipalName,
    bool Enabled,
    string DistinguishedName,
    IReadOnlyList<string> Groups);

public sealed record AdUserSearchResult(
    bool DomainJoined,
    string? DomainName,
    string? BaseDistinguishedName,
    IReadOnlyList<AdUser> Users,
    bool Truncated);

/// <summary>
/// LDAP user listing, optionally scoped to an OU (the base DN of the search).
/// Read-only like everything behind IDirectoryReader; memberOf is returned as
/// plain group CNs so callers can show which access a user has.
/// </summary>
internal sealed class UserSearchService
{
    private readonly DomainContextService _domainContextService;
    private readonly IDirectoryReader _directoryReader;
    private readonly ActiveDirectoryOptions _options;

    public UserSearchService(
        DomainContextService domainContextService,
        IDirectoryReader directoryReader,
        IOptions<ActiveDirectoryOptions> options)
    {
        _domainContextService = domainContextService;
        _directoryReader = directoryReader;
        _options = options.Value;
    }

    public async Task<Result<AdUserSearchResult>> SearchAsync(
        DirectoryConnection connection,
        string? baseDistinguishedName,
        bool includeDisabled,
        CancellationToken cancellationToken)
    {
        Result<DomainContext> context =
            await _domainContextService.GetContextAsync(connection, cancellationToken);
        if (context.IsFailure)
        {
            return Result.Failure<AdUserSearchResult>(context.Error!);
        }

        if (!context.Value.DomainJoined)
        {
            return Result.Success(new AdUserSearchResult(false, null, null, [], Truncated: false));
        }

        string baseDn = string.IsNullOrWhiteSpace(baseDistinguishedName)
            ? context.Value.DefaultNamingContext!
            : baseDistinguishedName.Trim();

        Result<BoundedDirectorySearchResult> entries = await _directoryReader.SearchBoundedAsync(
            new DirectorySearchQuery(
                context.Value.DomainName!,
                baseDn,
                includeDisabled ? AdFilters.Users : AdFilters.EnabledUsers,
                ["displayName", "sAMAccountName", "userPrincipalName", "userAccountControl", "memberOf"],
                DirectorySearchScope.Subtree,
                _options.PageSize,
                _options.SearchTimeout,
                connection.Server,
                connection.Credentials),
            _options.UserSearchLimit,
            cancellationToken);
        if (entries.IsFailure)
        {
            return Result.Failure<AdUserSearchResult>(entries.Error!);
        }

        List<AdUser> users = [.. entries.Value.Entries
            .Select(entry => new AdUser(
                entry.GetFirstValue("displayName") ?? entry.GetFirstValue("sAMAccountName") ?? entry.DistinguishedName,
                entry.GetFirstValue("sAMAccountName"),
                entry.GetFirstValue("userPrincipalName"),
                Enabled: ((entry.GetLong("userAccountControl") ?? 0) & AdFilters.UacAccountDisabled) == 0,
                entry.DistinguishedName,
                Groups: [.. entry.GetValues("memberOf")
                    .Select(FirstRdnValue)
                    .OrderBy(group => group, StringComparer.OrdinalIgnoreCase)]))
            .OrderBy(user => user.Name, StringComparer.OrdinalIgnoreCase)];

        bool truncated = entries.Value.TotalCount > users.Count;
        return Result.Success(new AdUserSearchResult(
            true,
            context.Value.DomainName,
            baseDn,
            users,
            truncated));
    }

    /// <summary>"CN=GG-App-Habel,OU=Groups,DC=..." → "GG-App-Habel" (good enough for display; escaped commas in CNs are rare).</summary>
    private static string FirstRdnValue(string distinguishedName)
    {
        string firstComponent = distinguishedName.Split(',')[0];
        int separatorIndex = firstComponent.IndexOf('=', StringComparison.Ordinal);
        return separatorIndex >= 0 ? firstComponent[(separatorIndex + 1)..] : firstComponent;
    }
}
