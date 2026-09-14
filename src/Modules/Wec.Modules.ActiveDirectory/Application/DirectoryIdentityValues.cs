using System.Security.Principal;
using Wec.Core.Abstractions;

namespace Wec.Modules.ActiveDirectory.Application;

internal static class DirectoryIdentityValues
{
    public static string? DirectoryScope(string namingContext)
    {
        string[] parts = namingContext.Split(',');
        return parts.Length > 0 && parts.All(part => part.StartsWith("DC=", StringComparison.OrdinalIgnoreCase)
            && part.Length > 3 && part[3..].All(character => char.IsAsciiLetterOrDigit(character) || character == '-'))
            ? string.Join('.', parts.Select(part => part[3..])).ToLowerInvariant() : null;
    }

    public static string? AccountSid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 184) { return null; }
        try
        {
            var sid = new SecurityIdentifier(value);
            return sid.IsAccountSid() ? sid.Value : null;
        }
        catch (ArgumentException) { return null; }
    }

    public static Guid? ObjectId(DirectoryEntryData entry)
    {
        byte[]? bytes = entry.GetBytes("objectGUID");
        return bytes is { Length: 16 } && new Guid(bytes) is var id && id != Guid.Empty ? id : null;
    }

    public static string? SecurityIdentifier(DirectoryEntryData entry)
    {
        byte[]? bytes = entry.GetBytes("objectSid");
        if (bytes is null)
        {
            return null;
        }
        try
        {
            return new SecurityIdentifier(bytes, 0).Value;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
