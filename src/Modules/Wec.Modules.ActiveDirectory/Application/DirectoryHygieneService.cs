using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.ActiveDirectory.Domain;

namespace Wec.Modules.ActiveDirectory.Application;

internal sealed partial class DirectoryHygieneService
{
    private const string InactiveUsersRuleId = "WEC-AD-INACTIVE-USERS";
    private const string InactiveComputersRuleId = "WEC-AD-INACTIVE-COMPUTERS";
    private const string PasswordNeverExpiresRuleId = "WEC-AD-PASSWORD-NEVER-EXPIRES";
    private const string DisabledPrivilegedRuleId = "WEC-AD-DISABLED-PRIVILEGED";
    private const string BuiltinAdministratorsSid = "S-1-5-32-544";
    private const int DomainAdminsRid = 512;
    private const int SchemaAdminsRid = 518;
    private const int EnterpriseAdminsRid = 519;

    private readonly DomainContextService _domainContextService;
    private readonly IDirectoryReader _directoryReader;
    private readonly IClock _clock;
    private readonly ActiveDirectoryOptions _options;
    private readonly ILogger<DirectoryHygieneService> _logger;

    // Scoped service, one bridge request per instance — set once per call
    private DirectoryConnection _connection = DirectoryConnection.Default;

    // DI requires a public constructor even on internal types
    public DirectoryHygieneService(
        DomainContextService domainContextService,
        IDirectoryReader directoryReader,
        IClock clock,
        IOptions<ActiveDirectoryOptions> options,
        ILogger<DirectoryHygieneService> logger)
    {
        _domainContextService = domainContextService;
        _directoryReader = directoryReader;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result<AdHygieneResult>> GetHygieneAsync(
        DirectoryConnection connection,
        CancellationToken cancellationToken)
    {
        _connection = connection;
        Result<DomainContext> context = await _domainContextService.GetContextAsync(connection, cancellationToken);
        if (context.IsFailure)
        {
            return Result.Failure<AdHygieneResult>(context.Error!);
        }

        if (!context.Value.DomainJoined)
        {
            return Result.Success(new AdHygieneResult(false, null, [], [], _clock.UtcNow));
        }

        string domainName = context.Value.DomainName!;
        string namingContext = context.Value.DefaultNamingContext!;
        long lastLogonCutoffFileTime = _clock.UtcNow.Subtract(_options.InactivityThreshold)
            .UtcDateTime.ToFileTimeUtc();

        Result<IReadOnlyList<PrivilegedGroupInfo>> privilegedGroups =
            await ReadPrivilegedGroupsAsync(domainName, namingContext, cancellationToken);
        if (privilegedGroups.IsFailure)
        {
            return Result.Failure<AdHygieneResult>(privilegedGroups.Error!);
        }

        var rules = new List<AdHygieneRule>();
        Result<AdHygieneRule> inactiveUsers = await EvaluateAccountRuleAsync(
            domainName,
            namingContext,
            InactiveUsersRuleId,
            $"Enabled users inactive for more than {_options.InactivityThreshold.TotalDays:0} days",
            AdFilters.InactiveUsers(lastLogonCutoffFileTime),
            "Review whether these accounts are still needed; disable or remove abandoned accounts.",
            cancellationToken);
        Result<AdHygieneRule> inactiveComputers = await EvaluateAccountRuleAsync(
            domainName,
            namingContext,
            InactiveComputersRuleId,
            $"Enabled computers inactive for more than {_options.InactivityThreshold.TotalDays:0} days",
            AdFilters.InactiveComputers(lastLogonCutoffFileTime),
            "Stale computer accounts widen the attack surface; confirm and remove decommissioned machines.",
            cancellationToken);
        Result<AdHygieneRule> passwordNeverExpires = await EvaluateAccountRuleAsync(
            domainName,
            namingContext,
            PasswordNeverExpiresRuleId,
            "Enabled users whose password never expires",
            AdFilters.EnabledPasswordNeverExpiresUsers,
            "Password-never-expires bypasses the password policy; reserve it for managed service accounts.",
            cancellationToken);
        Result<AdHygieneRule> disabledPrivileged = await EvaluateDisabledPrivilegedRuleAsync(
            domainName, namingContext, privilegedGroups.Value, cancellationToken);

        foreach (Result<AdHygieneRule> rule in new[]
                 {
                     inactiveUsers, inactiveComputers, passwordNeverExpires, disabledPrivileged,
                 })
        {
            if (rule.IsFailure)
            {
                return Result.Failure<AdHygieneResult>(rule.Error!);
            }

            rules.Add(rule.Value);
        }

        LogHygieneCaptured(domainName, rules.Count, privilegedGroups.Value.Count);
        return Result.Success(new AdHygieneResult(
            true, domainName, privilegedGroups.Value, rules, _clock.UtcNow));
    }

    public async Task<Result<AdHygieneRulePage>> GetRulePageAsync(
        DirectoryConnection connection,
        string ruleId,
        DateTimeOffset evaluatedAtUtc,
        string? query,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (ruleId is not InactiveUsersRuleId
            and not InactiveComputersRuleId
            and not PasswordNeverExpiresRuleId
            and not DisabledPrivilegedRuleId)
        {
            return InvalidRulePage($"Unknown directory hygiene rule '{ruleId}'.");
        }

        if (page < 1 || pageSize is < 1 or > 100)
        {
            return InvalidRulePage("Page must be at least 1 and pageSize must be between 1 and 100.");
        }

        string normalizedQuery = query?.Trim() ?? string.Empty;
        if (normalizedQuery.Length > 100)
        {
            return InvalidRulePage("The hygiene account search must not exceed 100 characters.");
        }

        long offset = ((long)page - 1) * pageSize;
        if (offset > int.MaxValue)
        {
            return InvalidRulePage("The requested hygiene result page is too large.");
        }

        _connection = connection;
        Result<DomainContext> context = await _domainContextService.GetContextAsync(connection, cancellationToken);
        if (context.IsFailure)
        {
            return Result.Failure<AdHygieneRulePage>(context.Error!);
        }

        if (!context.Value.DomainJoined)
        {
            return Result.Success(new AdHygieneRulePage(
                ruleId, page, pageSize, 0, [], evaluatedAtUtc));
        }

        string domainName = context.Value.DomainName!;
        string namingContext = context.Value.DefaultNamingContext!;
        long cutoffFileTime = evaluatedAtUtc.Subtract(_options.InactivityThreshold)
            .UtcDateTime.ToFileTimeUtc();
        string ldapFilter;
        switch (ruleId)
        {
            case InactiveUsersRuleId:
                ldapFilter = AdFilters.InactiveUsers(cutoffFileTime);
                break;
            case InactiveComputersRuleId:
                ldapFilter = AdFilters.InactiveComputers(cutoffFileTime);
                break;
            case PasswordNeverExpiresRuleId:
                ldapFilter = AdFilters.EnabledPasswordNeverExpiresUsers;
                break;
            default:
                Result<IReadOnlyList<PrivilegedGroupInfo>> privilegedGroups =
                    await ReadPrivilegedGroupsAsync(domainName, namingContext, cancellationToken);
                if (privilegedGroups.IsFailure)
                {
                    return Result.Failure<AdHygieneRulePage>(privilegedGroups.Error!);
                }

                if (privilegedGroups.Value.Count == 0)
                {
                    return Result.Success(new AdHygieneRulePage(
                        ruleId, page, pageSize, 0, [], evaluatedAtUtc));
                }

                ldapFilter = AdFilters.DisabledDirectMembersOfGroups(
                    privilegedGroups.Value.Select(group => group.DistinguishedName));
                break;
        }

        DirectorySearchQuery pageQuery = BuildQuery(
            domainName,
            namingContext,
            AdFilters.WithAccountNameSearch(ldapFilter, normalizedQuery),
            ["sAMAccountName", "lastLogonTimestamp"],
            sortAttribute: "sAMAccountName");
        Result<BoundedDirectorySearchResult> entries = await _directoryReader.SearchPageAsync(
            pageQuery,
            (int)offset,
            pageSize,
            cancellationToken);
        if (entries.IsFailure)
        {
            return Result.Failure<AdHygieneRulePage>(entries.Error!);
        }

        return Result.Success(new AdHygieneRulePage(
            ruleId,
            page,
            pageSize,
            entries.Value.TotalCount,
            [.. entries.Value.Entries.Select(ToAccountInfo)],
            evaluatedAtUtc));
    }

    public async Task<Result<AdPrivilegedGroupMemberPage>> GetPrivilegedGroupMemberPageAsync(
        DirectoryConnection connection,
        string groupDistinguishedName,
        string? query,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        string normalizedGroupDn = groupDistinguishedName?.Trim() ?? string.Empty;
        string normalizedQuery = query?.Trim() ?? string.Empty;
        if (normalizedGroupDn.Length is 0 or > 2_048)
        {
            return InvalidPrivilegedGroupPage("A valid privileged group distinguished name is required.");
        }

        if (normalizedQuery.Length > 100)
        {
            return InvalidPrivilegedGroupPage("The privileged group member search must not exceed 100 characters.");
        }

        if (page < 1 || pageSize is < 1 or > 100)
        {
            return InvalidPrivilegedGroupPage("Page must be at least 1 and pageSize must be between 1 and 100.");
        }

        long offset = ((long)page - 1) * pageSize;
        if (offset > int.MaxValue)
        {
            return InvalidPrivilegedGroupPage("The requested privileged group member page is too large.");
        }

        _connection = connection;
        Result<DomainContext> context = await _domainContextService.GetContextAsync(connection, cancellationToken);
        if (context.IsFailure)
        {
            return Result.Failure<AdPrivilegedGroupMemberPage>(context.Error!);
        }

        if (!context.Value.DomainJoined)
        {
            return InvalidPrivilegedGroupPage("Privileged group members require an Active Directory domain.");
        }

        string domainName = context.Value.DomainName!;
        string namingContext = context.Value.DefaultNamingContext!;
        Result<IReadOnlyList<ResolvedPrivilegedGroup>> resolvedGroups =
            await ResolvePrivilegedGroupsAsync(domainName, namingContext, cancellationToken);
        if (resolvedGroups.IsFailure)
        {
            return Result.Failure<AdPrivilegedGroupMemberPage>(resolvedGroups.Error!);
        }

        ResolvedPrivilegedGroup? selectedGroup = resolvedGroups.Value.FirstOrDefault(group =>
            string.Equals(group.DistinguishedName, normalizedGroupDn, StringComparison.OrdinalIgnoreCase));
        if (selectedGroup is null)
        {
            return InvalidPrivilegedGroupPage("The requested group is not one of the SID-validated privileged groups.");
        }

        DirectorySearchQuery memberQuery = BuildQuery(
            domainName,
            namingContext,
            AdFilters.WithDirectoryIdentitySearch(
                AdFilters.DirectMembersOfGroup(selectedGroup.DistinguishedName),
                normalizedQuery),
            ["sAMAccountName", "objectClass", "userAccountControl", "lastLogonTimestamp"],
            sortAttribute: "sAMAccountName");
        Result<BoundedDirectorySearchResult> entries = await _directoryReader.SearchPageAsync(
            memberQuery,
            (int)offset,
            pageSize,
            cancellationToken);
        if (entries.IsFailure)
        {
            return Result.Failure<AdPrivilegedGroupMemberPage>(entries.Error!);
        }

        return Result.Success(new AdPrivilegedGroupMemberPage(
            selectedGroup.GroupName,
            selectedGroup.DistinguishedName,
            page,
            pageSize,
            entries.Value.TotalCount,
            [.. entries.Value.Entries.Select(ToPrivilegedGroupMember)]));
    }

    private static Result<AdHygieneRulePage> InvalidRulePage(string message) =>
        Result.Failure<AdHygieneRulePage>(new Error(ErrorCode.InvalidRequest, message));

    private static Result<AdPrivilegedGroupMemberPage> InvalidPrivilegedGroupPage(string message) =>
        Result.Failure<AdPrivilegedGroupMemberPage>(new Error(ErrorCode.InvalidRequest, message));

    private async Task<Result<AdHygieneRule>> EvaluateAccountRuleAsync(
        string domainName,
        string namingContext,
        string ruleId,
        string title,
        string ldapFilter,
        string recommendation,
        CancellationToken cancellationToken)
    {
        Result<BoundedDirectorySearchResult> entries = await _directoryReader.SearchBoundedAsync(
            BuildQuery(domainName, namingContext, ldapFilter, ["sAMAccountName", "lastLogonTimestamp"]),
            _options.ExampleLimit,
            cancellationToken);
        if (entries.IsFailure)
        {
            return Result.Failure<AdHygieneRule>(entries.Error!);
        }

        return Result.Success(new AdHygieneRule(
            ruleId,
            title,
            entries.Value.TotalCount,
            [.. entries.Value.Entries.Select(ToAccountInfo)],
            recommendation));
    }

    private async Task<Result<IReadOnlyList<PrivilegedGroupInfo>>> ReadPrivilegedGroupsAsync(
        string domainName,
        string namingContext,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<ResolvedPrivilegedGroup>> resolvedGroups =
            await ResolvePrivilegedGroupsAsync(domainName, namingContext, cancellationToken);
        if (resolvedGroups.IsFailure)
        {
            return Result.Failure<IReadOnlyList<PrivilegedGroupInfo>>(resolvedGroups.Error!);
        }

        var groups = new List<PrivilegedGroupInfo>();
        foreach (ResolvedPrivilegedGroup group in resolvedGroups.Value)
        {
            Result<BoundedDirectorySearchResult> members = await _directoryReader.SearchBoundedAsync(
                BuildQuery(
                    domainName,
                    namingContext,
                    AdFilters.DirectMembersOfGroup(group.DistinguishedName),
                    []),
                _options.ExampleLimit,
                cancellationToken);
            if (members.IsFailure)
            {
                return Result.Failure<IReadOnlyList<PrivilegedGroupInfo>>(members.Error!);
            }

            groups.Add(new PrivilegedGroupInfo(
                group.GroupName,
                group.DistinguishedName,
                members.Value.TotalCount,
                [.. members.Value.Entries.Select(entry => entry.DistinguishedName)]));
        }

        return Result.Success<IReadOnlyList<PrivilegedGroupInfo>>(groups);
    }

    private async Task<Result<IReadOnlyList<ResolvedPrivilegedGroup>>> ResolvePrivilegedGroupsAsync(
        string domainName,
        string namingContext,
        CancellationToken cancellationToken)
    {
        Result<string> domainSid = await ReadDomainSidAsync(domainName, namingContext, cancellationToken);
        if (domainSid.IsFailure)
        {
            return Result.Failure<IReadOnlyList<ResolvedPrivilegedGroup>>(domainSid.Error!);
        }

        string[] privilegedSids =
        [
            $"{domainSid.Value}-{DomainAdminsRid}",
            $"{domainSid.Value}-{EnterpriseAdminsRid}",
            $"{domainSid.Value}-{SchemaAdminsRid}",
            BuiltinAdministratorsSid,
        ];

        var groups = new List<ResolvedPrivilegedGroup>();
        foreach (string sid in privilegedSids)
        {
            Result<IReadOnlyList<DirectoryEntryData>> entries = await _directoryReader.SearchAsync(
                BuildQuery(domainName, namingContext, AdFilters.GroupBySid(sid), ["sAMAccountName"]),
                cancellationToken);
            if (entries.IsFailure)
            {
                return Result.Failure<IReadOnlyList<ResolvedPrivilegedGroup>>(entries.Error!);
            }

            // Enterprise/Schema Admins only exist on the forest root — an absent
            // group is a valid answer for child domains, not an error
            if (entries.Value.Count == 0)
            {
                continue;
            }

            DirectoryEntryData group = entries.Value[0];
            groups.Add(new ResolvedPrivilegedGroup(
                group.GetFirstValue("sAMAccountName") ?? group.DistinguishedName,
                group.DistinguishedName));
        }

        return Result.Success<IReadOnlyList<ResolvedPrivilegedGroup>>(groups);
    }

    private async Task<Result<AdHygieneRule>> EvaluateDisabledPrivilegedRuleAsync(
        string domainName,
        string namingContext,
        IReadOnlyList<PrivilegedGroupInfo> privilegedGroups,
        CancellationToken cancellationToken)
    {
        const string ruleId = DisabledPrivilegedRuleId;
        const string title = "Disabled accounts that are still direct members of privileged groups";
        const string recommendation =
            "Disabled accounts keep their group SIDs when re-enabled; remove them from privileged groups instead of only disabling.";

        if (privilegedGroups.Count == 0)
        {
            return Result.Success(new AdHygieneRule(ruleId, title, 0, [], recommendation));
        }

        Result<BoundedDirectorySearchResult> entries = await _directoryReader.SearchBoundedAsync(
            BuildQuery(
                domainName,
                namingContext,
                AdFilters.DisabledDirectMembersOfGroups(
                    privilegedGroups.Select(group => group.DistinguishedName)),
                ["sAMAccountName"]),
            _options.ExampleLimit,
            cancellationToken);
        if (entries.IsFailure)
        {
            return Result.Failure<AdHygieneRule>(entries.Error!);
        }

        return Result.Success(new AdHygieneRule(
            ruleId,
            title,
            entries.Value.TotalCount,
            [.. entries.Value.Entries.Select(ToAccountInfo)],
            recommendation));
    }

    private async Task<Result<string>> ReadDomainSidAsync(
        string domainName,
        string namingContext,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<DirectoryEntryData>> domainHead = await _directoryReader.SearchAsync(
            new DirectorySearchQuery(
                domainName,
                namingContext,
                "(objectClass=*)",
                ["objectSid"],
                DirectorySearchScope.Base,
                _options.PageSize,
                _options.SearchTimeout,
                _connection.Server,
                _connection.Credentials),
            cancellationToken);
        if (domainHead.IsFailure)
        {
            return Result.Failure<string>(domainHead.Error!);
        }

        byte[]? sidBytes = domainHead.Value.Count > 0 ? domainHead.Value[0].GetBytes("objectSid") : null;
        if (sidBytes is null)
        {
            return Result.Failure<string>(new Error(
                ErrorCode.DirectoryUnavailable,
                "The domain head did not expose a readable objectSid."));
        }

        return Result.Success(new SecurityIdentifier(sidBytes, 0).Value);
    }

    private static AdAccountInfo ToAccountInfo(DirectoryEntryData entry) => new(
        entry.GetFirstValue("sAMAccountName") ?? entry.DistinguishedName,
        entry.DistinguishedName,
        entry.GetLong("lastLogonTimestamp") is { } fileTime
            ? new DateTimeOffset(DateTime.FromFileTimeUtc(fileTime), TimeSpan.Zero)
            : null);

    private static AdPrivilegedGroupMember ToPrivilegedGroupMember(DirectoryEntryData entry)
    {
        IReadOnlyList<string> objectClasses = entry.GetValues("objectClass");
        string entityType = objectClasses.Contains("computer", StringComparer.OrdinalIgnoreCase)
            ? "Computer"
            : objectClasses.Contains("group", StringComparer.OrdinalIgnoreCase)
                ? "Group"
                : objectClasses.Contains("foreignSecurityPrincipal", StringComparer.OrdinalIgnoreCase)
                    ? "Foreign principal"
                    : objectClasses.Contains("user", StringComparer.OrdinalIgnoreCase)
                        ? "User"
                        : "Directory object";
        string? accountStatus = entityType is "User" or "Computer"
            ? entry.GetLong("userAccountControl") is { } userAccountControl
                ? (userAccountControl & AdFilters.UacAccountDisabled) != 0 ? "Disabled" : "Enabled"
                : "Unknown"
            : null;

        return new AdPrivilegedGroupMember(
            entry.GetFirstValue("sAMAccountName"),
            entry.DistinguishedName,
            entityType,
            accountStatus,
            entry.GetLong("lastLogonTimestamp") is { } fileTime
                ? new DateTimeOffset(DateTime.FromFileTimeUtc(fileTime), TimeSpan.Zero)
                : null);
    }

    private DirectorySearchQuery BuildQuery(
        string domainName,
        string baseDistinguishedName,
        string ldapFilter,
        IReadOnlyList<string> attributes,
        string? sortAttribute = null) =>
        new(
            domainName,
            baseDistinguishedName,
            ldapFilter,
            attributes,
            DirectorySearchScope.Subtree,
            _options.PageSize,
            _options.SearchTimeout,
            _connection.Server,
            _connection.Credentials,
            sortAttribute);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "AD hygiene captured for {DomainName}: {RuleCount} rules, {PrivilegedGroupCount} privileged groups")]
    private partial void LogHygieneCaptured(string domainName, int ruleCount, int privilegedGroupCount);

    private sealed record ResolvedPrivilegedGroup(string GroupName, string DistinguishedName);
}
