using System.Net;
using Wec.Core.Targets;

namespace Wec.Modules.EmployeeLifecycle.Application;

internal sealed class DeviceIdentityIndex
{
    private readonly HashSet<string> _canonicalKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _aliasOwners = new(StringComparer.OrdinalIgnoreCase);

    public void AddAnchor(string host, params string?[] provenAliases)
    {
        string canonicalKey = DeviceIdentity.NormalizeHost(host);
        _canonicalKeys.Add(canonicalKey);
        AddAliasOwner(canonicalKey, canonicalKey);
        foreach (string? alias in provenAliases)
        {
            if (!string.IsNullOrWhiteSpace(alias))
            {
                AddAliasOwner(DeviceIdentity.NormalizeHost(alias), canonicalKey);
            }
        }
    }

    public string Resolve(string host)
    {
        string candidate = DeviceIdentity.NormalizeHost(host);
        if (_canonicalKeys.Contains(candidate))
        {
            return candidate;
        }

        if (_aliasOwners.TryGetValue(candidate, out HashSet<string>? owners) && owners.Count == 1)
        {
            return owners.Single();
        }

        _canonicalKeys.Add(candidate);
        AddAliasOwner(candidate, candidate);
        return candidate;
    }

    public static bool IsQualifiedHost(string host)
    {
        string normalized = DeviceIdentity.NormalizeHost(host);
        return IPAddress.TryParse(normalized, out _) || normalized.Contains('.', StringComparison.Ordinal);
    }

    private void AddAliasOwner(string alias, string canonicalKey)
    {
        if (!_aliasOwners.TryGetValue(alias, out HashSet<string>? owners))
        {
            owners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _aliasOwners[alias] = owners;
        }

        owners.Add(canonicalKey);
    }
}
