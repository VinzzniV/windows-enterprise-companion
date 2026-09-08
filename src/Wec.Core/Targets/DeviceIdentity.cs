using System.Net;
using System.Net.NetworkInformation;

namespace Wec.Core.Targets;

public static class DeviceIdentity
{
    public static string NormalizeHost(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        string value = host.Trim().TrimEnd('.');
        if (value.Length == 0)
        {
            throw new ArgumentException("Host must contain a name or address.", nameof(host));
        }

        return IPAddress.TryParse(value, out IPAddress? address)
            ? address.ToString().ToUpperInvariant()
            : value.ToUpperInvariant();
    }

    public static string? GetShortDnsAlias(string host)
    {
        string normalized = NormalizeHost(host);
        if (IPAddress.TryParse(normalized, out _))
        {
            return null;
        }

        int dot = normalized.IndexOf('.', StringComparison.Ordinal);
        return dot > 0 ? normalized[..dot] : normalized;
    }

    public static bool IsLocalHost(string host)
    {
        string candidate = NormalizeHost(host);
        return LocalHostAliases().Contains(candidate, StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<string> LocalHostAliases()
    {
        string machineName = NormalizeHost(Environment.MachineName);
        var aliases = new List<string> { machineName };
        try
        {
            string domainName = IPGlobalProperties.GetIPGlobalProperties().DomainName.Trim().Trim('.');
            if (domainName.Length > 0)
            {
                aliases.Add(NormalizeHost($"{machineName}.{domainName}"));
            }
        }
        catch (NetworkInformationException)
        {
            // Local DNS configuration can be unavailable without making the short machine name unusable.
        }

        return aliases;
    }
}
