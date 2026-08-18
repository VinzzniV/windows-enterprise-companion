using System.Net;
using System.Net.Sockets;

namespace Wec.Modules.NetworkScan.Application;

/// <summary>
/// Membership test for the scanned target, used to spot reserved-but-dead IPs
/// (stale DHCP reservations) that fall inside the range. Only CIDR and single-IP
/// tokens are understood; octet ranges and host names contribute nothing, so
/// stale detection simply does not fire for those inputs.
/// </summary>
internal sealed class ScannedRange
{
    private readonly List<(uint Network, uint Mask)> _cidrs = [];
    private readonly HashSet<uint> _singles = [];

    public static ScannedRange Parse(string target)
    {
        var range = new ScannedRange();
        foreach (string token in target.Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int slash = token.IndexOf('/');
            if (slash >= 0)
            {
                if (int.TryParse(token[(slash + 1)..], out int prefix)
                    && prefix is >= 0 and <= 32
                    && TryToUint(token[..slash], out uint network))
                {
                    uint mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
                    range._cidrs.Add((network & mask, mask));
                }
            }
            else if (TryToUint(token, out uint single))
            {
                range._singles.Add(single);
            }
        }

        return range;
    }

    public bool Contains(string ip)
    {
        if (!TryToUint(ip, out uint value))
        {
            return false;
        }

        if (_singles.Contains(value))
        {
            return true;
        }

        foreach ((uint network, uint mask) in _cidrs)
        {
            if ((value & mask) == network)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryToUint(string ip, out uint value)
    {
        value = 0;
        if (!IPAddress.TryParse(ip, out IPAddress? address)
            || address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        byte[] bytes = address.GetAddressBytes();
        value = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
        return true;
    }
}
