using System.Security.Principal;
using Wec.Core.Abstractions;

namespace Wec.Modules.ActiveDirectory.Application;

internal static class DirectoryIdentityValues
{
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
