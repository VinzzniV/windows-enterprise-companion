namespace Wec.Core.Targets;

public enum CredentialMode
{
    CurrentUser = 0,
    Explicit,
}

/// <summary>
/// Credentials for a scan. Explicit passwords live in memory for the duration
/// of the request only — they are never persisted (ADR 0007).
/// </summary>
public sealed record ScanCredentials
{
    public static readonly ScanCredentials CurrentUser = new(CredentialMode.CurrentUser, null, null, null);

    private ScanCredentials(CredentialMode mode, string? userName, string? domain, string? password)
    {
        Mode = mode;
        UserName = userName;
        Domain = domain;
        Password = password;
    }

    public CredentialMode Mode { get; }

    public string? UserName { get; }

    public string? Domain { get; }

    public string? Password { get; }

    public static ScanCredentials Explicit(string userName, string? domain, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentNullException.ThrowIfNull(password);
        return new ScanCredentials(CredentialMode.Explicit, userName.Trim(), NormalizeDomain(domain), password);
    }

    private static string? NormalizeDomain(string? domain) =>
        string.IsNullOrWhiteSpace(domain) ? null : domain.Trim();
}
