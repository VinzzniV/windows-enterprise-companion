using Wec.Core.Results;
using Wec.Core.Targets;

namespace Wec.Modules.ActiveDirectory.Application;

/// <summary>How to reach the directory: which domain, optionally which DC, with which identity.</summary>
internal sealed record DirectoryConnection(
    string? DomainOverride,
    string? Server,
    ScanCredentials Credentials)
{
    public static readonly DirectoryConnection Default = new(null, null, ScanCredentials.CurrentUser);
}

/// <summary>
/// Bridge payload for the AD actions. All fields optional: without them the
/// analysis targets the machine's own domain with the current identity
/// (ADR 0006). UserDomain is the credential's domain, Domain the directory
/// to analyze.
/// </summary>
public sealed record DirectoryConnectionRequest(
    string? Domain = null,
    string? Server = null,
    string? UserName = null,
    string? UserDomain = null,
    string? Password = null)
{
    internal Result<DirectoryConnection> ToConnection()
    {
        ScanCredentials credentials;
        if (string.IsNullOrWhiteSpace(UserName))
        {
            credentials = ScanCredentials.CurrentUser;
        }
        else if (Password is null)
        {
            return Result.Failure<DirectoryConnection>(new Error(
                ErrorCode.InvalidRequest,
                "Explicit credentials require a password."));
        }
        else
        {
            Result<ScanCredentials> normalized = NormalizeCredentials(UserName, UserDomain, Password, Domain);
            if (normalized.IsFailure)
            {
                return Result.Failure<DirectoryConnection>(normalized.Error!);
            }

            credentials = normalized.Value;
        }

        return Result.Success(new DirectoryConnection(
            string.IsNullOrWhiteSpace(Domain) ? null : Domain.Trim(),
            string.IsNullOrWhiteSpace(Server) ? null : Server.Trim(),
            credentials));
    }

    /// <summary>
    /// Accepts every common account notation and produces one canonical
    /// credential: UPN stays as-is (the name carries its domain),
    /// DOMAIN\user is split (an embedded domain wins over the field), a plain
    /// user needs a credential domain — falling back to the directory domain
    /// when one is set.
    /// </summary>
    internal static Result<ScanCredentials> NormalizeCredentials(
        string userName,
        string? credentialDomain,
        string password,
        string? directoryDomain) =>
        DirectoryScanCredentials.NormalizeExplicit(
            userName, credentialDomain, password, directoryDomain);
}
