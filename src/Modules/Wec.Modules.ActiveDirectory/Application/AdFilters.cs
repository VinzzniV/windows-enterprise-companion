using System.Globalization;

namespace Wec.Modules.ActiveDirectory.Application;

/// <summary>
/// LDAP filters used by the module. userAccountControl bits are matched
/// server-side with the LDAP_MATCHING_RULE_BIT_AND rule; SID-based filters
/// keep the checks independent of localized group names (a German domain
/// calls Domain Admins "Domänen-Admins").
/// </summary>
internal static class AdFilters
{
    private const string UacBitAnd = "userAccountControl:1.2.840.113556.1.4.803:=";

    public const int UacAccountDisabled = 2;
    public const int UacServerTrustAccount = 8192;
    public const int UacPasswordNeverExpires = 65536;

    public const string Users = "(&(objectCategory=person)(objectClass=user))";
    public const string EnabledUsers =
        $"(&(objectCategory=person)(objectClass=user)(!({UacBitAnd}2)))";
    public const string DisabledUsers =
        $"(&(objectCategory=person)(objectClass=user)({UacBitAnd}2))";
    public const string Groups = "(objectCategory=group)";
    public const string Computers = "(objectCategory=computer)";
    public const string DomainControllers = $"(&(objectCategory=computer)({UacBitAnd}8192))";
    public const string EnabledPasswordNeverExpiresUsers =
        $"(&(objectCategory=person)(objectClass=user)({UacBitAnd}65536)(!({UacBitAnd}2)))";

    public static string InactiveUsers(long lastLogonCutoffFileTime) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"(&(objectCategory=person)(objectClass=user)(!({UacBitAnd}2))(lastLogonTimestamp<={lastLogonCutoffFileTime}))");

    public static string InactiveComputers(long lastLogonCutoffFileTime) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"(&(objectCategory=computer)(!({UacBitAnd}2))(lastLogonTimestamp<={lastLogonCutoffFileTime}))");

    public static string GroupBySid(string sidSddl) => $"(&(objectCategory=group)(objectSid={sidSddl}))";

    public static string DisabledDirectMembersOfGroups(IEnumerable<string> groupDistinguishedNames)
    {
        string memberOfClauses = string.Concat(
            groupDistinguishedNames.Select(dn => $"(memberOf={EscapeFilterValue(dn)})"));
        return $"(&(objectCategory=person)(objectClass=user)({UacBitAnd}2)(|{memberOfClauses}))";
    }

    public static string DirectMembersOfGroup(string groupDistinguishedName) =>
        $"(&(objectClass=*)(memberOf={EscapeFilterValue(groupDistinguishedName)}))";

    public static string WithDirectoryIdentitySearch(string baseFilter, string? query) =>
        WithAccountNameSearch(baseFilter, query);

    public static string WithAccountNameSearch(string baseFilter, string? query)
    {
        string trimmed = query?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return baseFilter;
        }

        string escaped = EscapeFilterValue(trimmed);
        return $"(&{baseFilter}(|(sAMAccountName=*{escaped}*)(cn=*{escaped}*)))";
    }

    /// <summary>
    /// Computer search by name/DNS name. A pattern without wildcards becomes
    /// a substring match (search-box semantics); user-typed '*' wildcards
    /// survive escaping, everything else is escaped per RFC 4515.
    /// </summary>
    public static string ComputersByName(string? namePattern, bool includeDisabled)
    {
        string disabledClause = includeDisabled ? string.Empty : $"(!({UacBitAnd}2))";
        string trimmed = namePattern?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return includeDisabled ? Computers : $"(&(objectCategory=computer){disabledClause})";
        }

        string escaped = EscapeFilterValueKeepingWildcards(trimmed);
        string pattern = escaped.Contains('*', StringComparison.Ordinal) ? escaped : $"*{escaped}*";
        return $"(&(objectCategory=computer)(|(name={pattern})(dNSHostName={pattern})){disabledClause})";
    }

    private static string EscapeFilterValueKeepingWildcards(string value) =>
        value
            .Replace(@"\", @"\5c", StringComparison.Ordinal)
            .Replace("(", @"\28", StringComparison.Ordinal)
            .Replace(")", @"\29", StringComparison.Ordinal)
            .Replace("\0", @"\00", StringComparison.Ordinal);

    /// <summary>RFC 4515 escaping for values embedded in LDAP filters.</summary>
    public static string EscapeFilterValue(string value) =>
        value
            .Replace(@"\", @"\5c", StringComparison.Ordinal)
            .Replace("*", @"\2a", StringComparison.Ordinal)
            .Replace("(", @"\28", StringComparison.Ordinal)
            .Replace(")", @"\29", StringComparison.Ordinal)
            .Replace("\0", @"\00", StringComparison.Ordinal);
}
