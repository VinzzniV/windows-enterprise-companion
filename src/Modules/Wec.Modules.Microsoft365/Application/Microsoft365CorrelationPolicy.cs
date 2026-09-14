using Wec.Core.Microsoft365;
using Wec.Modules.Microsoft365.Domain;

namespace Wec.Modules.Microsoft365.Application;

internal static class Microsoft365CorrelationPolicy
{
    internal static Microsoft365Correlation User(IEnumerable<Microsoft365User> users, string? sid, string? upn, bool completeUserSet = true)
    {
        Microsoft365User[] all = users.Where(user => ValidId(user.Id)).ToArray();
        Microsoft365User[] matches = !ValidSid(sid) ? [] : all.Where(user =>
            string.Equals(user.OnPremisesSid, sid, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 1)
        {
            if (!completeUserSet)
            {
                return new("Candidate", "Exact SID in a partial user set; uniqueness is not established. Load complete bounded evidence or resolve this SID explicitly.", matches[0], null, null, null, false);
            }
            return new("Matched", "Exact AD SID / Entra onPremisesSecurityIdentifier match. AD remains the identity authority.", matches[0], null, null, null, false);
        }
        if (matches.Length > 1) { return Unknown("Ambiguous", "Multiple Entra users share this SID. No automatic relationship is selected."); }
        matches = string.IsNullOrWhiteSpace(upn) ? [] : all.Where(user =>
            string.Equals(user.UserPrincipalName, upn, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 1 && ValidSid(sid) && !string.IsNullOrWhiteSpace(matches[0].OnPremisesSid))
        {
            return Unknown("Conflict", "The UPN matches but the on-premises SID differs. These accounts must not be automatically linked.");
        }
        return matches.Length == 1
            ? new("Candidate", "UPN matches, but UPN is mutable. Verify synchronization identity before treating these accounts as one identity.", matches[0], null, null, null, false)
            : Unknown(matches.Length > 1 ? "Ambiguous" : "Not matched", "No unique SID relationship in the loaded Entra evidence. Missing evidence does not prove the account is absent.");
    }

    internal static Microsoft365Correlation Device(IEnumerable<Microsoft365Device> devices,
        IEnumerable<Microsoft365ManagedDevice> managedDevices, string? host, string? entraDeviceId)
    {
        bool stable = ValidId(entraDeviceId);
        Microsoft365Device[] matches = devices.Where(device => stable
            ? SameId(device.DeviceId, entraDeviceId)
            : !string.IsNullOrWhiteSpace(host) && string.Equals(device.DisplayName, host, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1)
        {
            return Unknown(matches.Length > 1 ? "Ambiguous" : "Not matched", "No unique Entra device relationship in the loaded evidence. WEC/AD computer names are not stable cloud identifiers.");
        }
        Microsoft365Device matched = matches[0];
        Microsoft365ManagedDevice[] managed = managedDevices.Where(device => SameId(device.EntraDeviceId, matched.DeviceId)).ToArray();
        if (managed.Length > 1)
        {
            return new("Ambiguous", "Entra device found, but multiple Intune records share its device ID. No Intune record is selected.", null, matched, null, null, false);
        }
        return new(stable ? "Matched" : "Candidate", stable
            ? "Exact Entra deviceId / Intune azureADDeviceId relationship; this does not prove primary user or ownership."
            : "Device name candidate only: WEC's stored Inventory and AD computer projection have no Entra device ID. Verify identity; do not interpret this as a confirmed join.",
            null, matched, managed.SingleOrDefault(), null, false);
    }

    private static bool ValidId(string? id) => Guid.TryParse(id, out Guid value) && value != Guid.Empty;
    private static bool ValidSid(string? sid)
    {
        string[] parts = sid?.Split('-') ?? [];
        return parts.Length == 8 && parts[0].Equals("S", StringComparison.OrdinalIgnoreCase)
            && parts[1] == "1" && parts[2] == "5" && parts[3] == "21"
            && parts.Skip(4).All(part => uint.TryParse(part, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out _));
    }
    private static bool SameId(string? left, string? right) => ValidId(left) && ValidId(right)
        && Guid.Parse(left!) == Guid.Parse(right!);
    private static Microsoft365Correlation Unknown(string state, string explanation) => new(state, explanation, null, null, null, null, false);
}
