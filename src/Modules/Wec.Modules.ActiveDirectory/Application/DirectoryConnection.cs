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
            credentials = ScanCredentials.Explicit(UserName, UserDomain, Password);
        }

        return Result.Success(new DirectoryConnection(
            string.IsNullOrWhiteSpace(Domain) ? null : Domain.Trim(),
            string.IsNullOrWhiteSpace(Server) ? null : Server.Trim(),
            credentials));
    }
}
