using System.Globalization;
using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Results;

namespace Wec.Modules.ActiveDirectory.Application;

internal static class DirectoryUserMapper
{
    public static readonly IReadOnlyList<string> Attributes =
    [
        "objectGUID", "objectSid", "displayName", "sAMAccountName", "userPrincipalName", "mail",
        "employeeID", "department", "title", "manager", "userAccountControl", "whenCreated",
        "accountExpires", "lastLogonTimestamp", "pwdLastSet", "msDS-UserPasswordExpiryTimeComputed", "memberOf",
    ];

    public static Result<IReadOnlyList<DirectoryUserRecord>> Map(IReadOnlyList<DirectoryEntryData> entries)
    {
        var users = new List<DirectoryUserRecord>(entries.Count);
        foreach (DirectoryEntryData entry in entries)
        {
            Result<DirectoryUserRecord> user = Map(entry);
            if (user.IsFailure)
            {
                return Result.Failure<IReadOnlyList<DirectoryUserRecord>>(user.Error!);
            }

            users.Add(user.Value);
        }

        return Result.Success<IReadOnlyList<DirectoryUserRecord>>(users);
    }

    public static Result<DirectoryUserRecord> Map(
        DirectoryEntryData entry,
        DirectoryUserPrivilegedAccess? privilegedAccess = null)
    {
        Guid? objectId = DirectoryIdentityValues.ObjectId(entry);
        if (objectId is null)
        {
            return Result.Failure<DirectoryUserRecord>(new Error(
                ErrorCode.DirectoryUnavailable,
                "Active Directory returned a user without a readable objectGUID."));
        }

        long? userAccountControl = entry.GetLong("userAccountControl");
        IReadOnlyList<DirectoryUserGroup> groups = [.. entry.GetValues("memberOf")
            .Select(groupDn => new DirectoryUserGroup(groupDn, FirstRdnValue(groupDn)))
            .OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase)];
        string displayName = entry.GetFirstValue("displayName")
            ?? entry.GetFirstValue("sAMAccountName")
            ?? entry.DistinguishedName;

        return Result.Success(new DirectoryUserRecord(
            objectId.Value,
            DirectoryIdentityValues.SecurityIdentifier(entry),
            displayName,
            entry.GetFirstValue("sAMAccountName"),
            entry.GetFirstValue("userPrincipalName"),
            entry.GetFirstValue("mail"),
            entry.GetFirstValue("employeeID"),
            entry.GetFirstValue("department"),
            entry.GetFirstValue("title"),
            entry.GetFirstValue("manager"),
            entry.DistinguishedName,
            ParentPath(entry.DistinguishedName),
            userAccountControl is { } uac ? (uac & AdFilters.UacAccountDisabled) == 0 : null,
            ParseGeneralizedTime(entry.GetFirstValue("whenCreated")),
            ParseFileTime(entry.GetLong("accountExpires"), treatNeverAsNull: true),
            ParseFileTime(entry.GetLong("lastLogonTimestamp")),
            ParseFileTime(entry.GetLong("pwdLastSet")),
            ParseFileTime(entry.GetLong("msDS-UserPasswordExpiryTimeComputed"), treatNeverAsNull: true),
            userAccountControl is { } passwordUac
                ? (passwordUac & AdFilters.UacPasswordNeverExpires) != 0
                : null,
            groups,
            privilegedAccess ?? new DirectoryUserPrivilegedAccess(
                DirectoryUserAccessCoverage.NotEvaluated,
                "Privileged access is evaluated only for an individual user profile.",
                [])));
    }

    private static DateTimeOffset? ParseFileTime(long? fileTime, bool treatNeverAsNull = false)
    {
        if (fileTime is null or <= 0 || (treatNeverAsNull && fileTime == long.MaxValue))
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromFileTime(fileTime.Value).ToUniversalTime();
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static DateTimeOffset? ParseGeneralizedTime(string? value)
    {
        string[] formats = ["yyyyMMddHHmmss'.0Z'", "yyyyMMddHHmmss'Z'", "yyyyMMddHHmmss.FFFFFFF'Z'"];
        return DateTimeOffset.TryParseExact(
            value,
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out DateTimeOffset result)
            ? result
            : null;
    }

    private static string ParentPath(string distinguishedName)
    {
        bool escaped = false;
        for (int index = 0; index < distinguishedName.Length; index++)
        {
            char character = distinguishedName[index];
            if (character == ',' && !escaped)
            {
                return distinguishedName[(index + 1)..];
            }

            escaped = character == '\\' && !escaped;
            if (character != '\\')
            {
                escaped = false;
            }
        }

        return string.Empty;
    }

    private static string FirstRdnValue(string distinguishedName)
    {
        string firstComponent = distinguishedName.Split(',')[0];
        int separatorIndex = firstComponent.IndexOf('=', StringComparison.Ordinal);
        return separatorIndex >= 0 ? firstComponent[(separatorIndex + 1)..] : firstComponent;
    }
}
