using System.Net;

namespace Wec.Core.Targets;

public static class HostAddress
{
    public static string ComparisonKey(string? host)
    {
        string value = host?.Trim() ?? string.Empty;
        if (IPAddress.TryParse(value, out IPAddress? address))
        {
            // Preserve ambiguous IPv4 shorthand/octal spellings and scoped IPv6
            // instead of silently changing an operational endpoint.
            if ((!value.Contains(':') && !string.Equals(value, address.ToString(), StringComparison.Ordinal))
                || value.Contains('%'))
            {
                return value.ToUpperInvariant();
            }
            return address.ToString().ToUpperInvariant();
        }

        return value.TrimEnd('.').ToUpperInvariant();
    }

    public static string? ShortNameAlias(string? host)
    {
        string value = host?.Trim() ?? string.Empty;
        if (value.Length == 0 || IPAddress.TryParse(value, out _))
        {
            return null;
        }

        return ComparisonKey(value).Split('.')[0];
    }

    public static bool IsExactLocalName(string host, string machineName) =>
        !string.IsNullOrWhiteSpace(machineName)
        && string.Equals(host.Trim(), machineName.Trim(), StringComparison.OrdinalIgnoreCase);
}
