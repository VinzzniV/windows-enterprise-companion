using Wec.Core.Results;

namespace Wec.Core.Targets;

/// <summary>
/// Canonicalizes the common Windows account notations used by directory-bound
/// read operations. Passwords remain only in the returned in-memory credential.
/// </summary>
public static class DirectoryScanCredentials
{
    public static Result<ScanCredentials> NormalizeExplicit(
        string userName,
        string? credentialDomain,
        string password,
        string? directoryDomain)
    {
        string trimmedUserName = userName.Trim();

        if (trimmedUserName.Contains('\\', StringComparison.Ordinal))
        {
            string[] parts = trimmedUserName.Split('\\', 2);
            if (string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
            {
                return Invalid($"'{trimmedUserName}' is not a valid DOMAIN\\user name.");
            }

            return Result.Success(ScanCredentials.Explicit(parts[1], parts[0], password));
        }

        if (trimmedUserName.Contains('@', StringComparison.Ordinal))
        {
            return Result.Success(ScanCredentials.Explicit(trimmedUserName, null, password));
        }

        if (!string.IsNullOrWhiteSpace(credentialDomain))
        {
            return Result.Success(ScanCredentials.Explicit(trimmedUserName, credentialDomain, password));
        }

        if (!string.IsNullOrWhiteSpace(directoryDomain))
        {
            return Result.Success(ScanCredentials.Explicit(trimmedUserName, directoryDomain, password));
        }

        return Invalid(
            "The account needs a domain: use user@domain.tld, DOMAIN\\user, or fill in the credential domain.");
    }

    private static Result<ScanCredentials> Invalid(string message) =>
        Result.Failure<ScanCredentials>(new Error(ErrorCode.InvalidRequest, message));
}
