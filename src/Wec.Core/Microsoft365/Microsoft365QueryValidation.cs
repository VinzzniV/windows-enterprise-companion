using System.Globalization;
using Wec.Core.Results;

namespace Wec.Core.Microsoft365;

public static class Microsoft365QueryValidation
{
    public static bool RequiresObjectId(Microsoft365Resource resource) => resource is not
        (Microsoft365Resource.Tenant or Microsoft365Resource.Users or Microsoft365Resource.Groups
        or Microsoft365Resource.Devices or Microsoft365Resource.ManagedDevices or Microsoft365Resource.Licenses or Microsoft365Resource.UsersBySid);

    public static string? AccountSid(string? sid)
    {
        string[] parts = sid?.Split('-') ?? [];
        if (parts.Length != 8 || !parts[0].Equals("S", StringComparison.OrdinalIgnoreCase)
            || parts[1] != "1" || parts[2] != "5" || parts[3] != "21"
            || !parts.Skip(4).All(part => uint.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
        {
            return null;
        }
        return "S-1-5-21-" + string.Join('-', parts.Skip(4).Select(part => uint.Parse(part, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)));
    }

    public static Result<Microsoft365Query> Normalize(Microsoft365Query query)
    {
        bool sidQuery = query.Resource == Microsoft365Resource.UsersBySid;
        string? sid = AccountSid(query.SecurityIdentifier);
        if (!Enum.IsDefined(query.Resource) || RequiresObjectId(query.Resource) != (query.ObjectId is not null)
            || query.ObjectId is not null && (!Guid.TryParse(query.ObjectId, out Guid id) || id == Guid.Empty)
            || sidQuery && sid is null || !sidQuery && query.SecurityIdentifier is not null)
        {
            return Result.Failure<Microsoft365Query>(new(ErrorCode.InvalidRequest,
                "Use an allowed Microsoft 365 resource with its required object GUID or exact AD account SID."));
        }
        return Result.Success(query with { ObjectId = query.ObjectId is null ? null : Guid.Parse(query.ObjectId).ToString("D"), SecurityIdentifier = sid });
    }
}
