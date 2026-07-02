using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.ActiveDirectory.Domain;

namespace Wec.Modules.ActiveDirectory.Application;

internal sealed partial class DirectoryHygieneService
{
    private const string BuiltinAdministratorsSid = "S-1-5-32-544";
    private const int DomainAdminsRid = 512;
    private const int SchemaAdminsRid = 518;
    private const int EnterpriseAdminsRid = 519;

    private readonly DomainContextService _domainContextService;
    private readonly IDirectoryReader _directoryReader;
    private readonly IClock _clock;
    private readonly ActiveDirectoryOptions _options;
    private readonly ILogger<DirectoryHygieneService> _logger;

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

    public async Task<Result<AdHygieneResult>> GetHygieneAsync(CancellationToken cancellationToken)
    {
        Result<DomainContext> context = await _domainContextService.GetContextAsync(cancellationToken);
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
            "WEC-AD-INACTIVE-USERS",
            $"Enabled users inactive for more than {_options.InactivityThreshold.TotalDays:0} days",
            AdFilters.InactiveUsers(lastLogonCutoffFileTime),
            "Review whether these accounts are still needed; disable or remove abandoned accounts.",
            cancellationToken);
        Result<AdHygieneRule> inactiveComputers = await EvaluateAccountRuleAsync(
            domainName,
            namingContext,
            "WEC-AD-INACTIVE-COMPUTERS",
            $"Enabled computers inactive for more than {_options.InactivityThreshold.TotalDays:0} days",
            AdFilters.InactiveComputers(lastLogonCutoffFileTime),
            "Stale computer accounts widen the attack surface; confirm and remove decommissioned machines.",
            cancellationToken);
        Result<AdHygieneRule> passwordNeverExpires = await EvaluateAccountRuleAsync(
            domainName,
            namingContext,
            "WEC-AD-PASSWORD-NEVER-EXPIRES",
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

    private async Task<Result<AdHygieneRule>> EvaluateAccountRuleAsync(
        string domainName,
        string namingContext,
        string ruleId,
        string title,
        string ldapFilter,
        string recommendation,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<DirectoryEntryData>> entries = await _directoryReader.SearchAsync(
            BuildQuery(domainName, namingContext, ldapFilter, ["sAMAccountName", "lastLogonTimestamp"]),
            cancellationToken);
        if (entries.IsFailure)
        {
            return Result.Failure<AdHygieneRule>(entries.Error!);
        }

        return Result.Success(new AdHygieneRule(
            ruleId,
            title,
            entries.Value.Count,
            [.. entries.Value.Take(_options.ExampleLimit).Select(ToAccountInfo)],
            recommendation));
    }

    private async Task<Result<IReadOnlyList<PrivilegedGroupInfo>>> ReadPrivilegedGroupsAsync(
        string domainName,
        string namingContext,
        CancellationToken cancellationToken)
    {
        Result<string> domainSid = await ReadDomainSidAsync(domainName, namingContext, cancellationToken);
        if (domainSid.IsFailure)
        {
            return Result.Failure<IReadOnlyList<PrivilegedGroupInfo>>(domainSid.Error!);
        }

        string[] privilegedSids =
        [
            $"{domainSid.Value}-{DomainAdminsRid}",
            $"{domainSid.Value}-{EnterpriseAdminsRid}",
            $"{domainSid.Value}-{SchemaAdminsRid}",
            BuiltinAdministratorsSid,
        ];

        var groups = new List<PrivilegedGroupInfo>();
        foreach (string sid in privilegedSids)
        {
            Result<IReadOnlyList<DirectoryEntryData>> entries = await _directoryReader.SearchAsync(
                BuildQuery(domainName, namingContext, AdFilters.GroupBySid(sid), ["sAMAccountName", "member"]),
                cancellationToken);
            if (entries.IsFailure)
            {
                return Result.Failure<IReadOnlyList<PrivilegedGroupInfo>>(entries.Error!);
            }

            // Enterprise/Schema Admins only exist on the forest root — an absent
            // group is a valid answer for child domains, not an error
            if (entries.Value.Count == 0)
            {
                continue;
            }

            DirectoryEntryData group = entries.Value[0];
            IReadOnlyList<string> members = group.GetValues("member");
            groups.Add(new PrivilegedGroupInfo(
                group.GetFirstValue("sAMAccountName") ?? group.DistinguishedName,
                group.DistinguishedName,
                members.Count,
                [.. members.Take(_options.ExampleLimit)]));
        }

        return Result.Success<IReadOnlyList<PrivilegedGroupInfo>>(groups);
    }

    private async Task<Result<AdHygieneRule>> EvaluateDisabledPrivilegedRuleAsync(
        string domainName,
        string namingContext,
        IReadOnlyList<PrivilegedGroupInfo> privilegedGroups,
        CancellationToken cancellationToken)
    {
        const string ruleId = "WEC-AD-DISABLED-PRIVILEGED";
        const string title = "Disabled accounts that are still direct members of privileged groups";
        const string recommendation =
            "Disabled accounts keep their group SIDs when re-enabled; remove them from privileged groups instead of only disabling.";

        if (privilegedGroups.Count == 0)
        {
            return Result.Success(new AdHygieneRule(ruleId, title, 0, [], recommendation));
        }

        Result<IReadOnlyList<DirectoryEntryData>> entries = await _directoryReader.SearchAsync(
            BuildQuery(
                domainName,
                namingContext,
                AdFilters.DisabledDirectMembersOfGroups(
                    privilegedGroups.Select(group => group.DistinguishedName)),
                ["sAMAccountName"]),
            cancellationToken);
        if (entries.IsFailure)
        {
            return Result.Failure<AdHygieneRule>(entries.Error!);
        }

        return Result.Success(new AdHygieneRule(
            ruleId,
            title,
            entries.Value.Count,
            [.. entries.Value.Take(_options.ExampleLimit).Select(ToAccountInfo)],
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
                _options.SearchTimeout),
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

    private DirectorySearchQuery BuildQuery(
        string domainName,
        string baseDistinguishedName,
        string ldapFilter,
        IReadOnlyList<string> attributes) =>
        new(
            domainName,
            baseDistinguishedName,
            ldapFilter,
            attributes,
            DirectorySearchScope.Subtree,
            _options.PageSize,
            _options.SearchTimeout);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "AD hygiene captured for {DomainName}: {RuleCount} rules, {PrivilegedGroupCount} privileged groups")]
    private partial void LogHygieneCaptured(string domainName, int ruleCount, int privilegedGroupCount);
}
