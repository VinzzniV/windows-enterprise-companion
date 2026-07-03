namespace Wec.Core.Targets;

public sealed record ScanTarget
{
    public static readonly ScanTarget Local = new(true, null);

    private ScanTarget(bool isLocal, string? host)
    {
        IsLocal = isLocal;
        Host = host;
    }

    public bool IsLocal { get; }

    /// <summary>Hostname, FQDN or IP address; null for the local machine.</summary>
    public string? Host { get; }

    public string DisplayName => IsLocal ? Environment.MachineName : Host!;

    public static ScanTarget Remote(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        return new ScanTarget(false, host.Trim());
    }
}
