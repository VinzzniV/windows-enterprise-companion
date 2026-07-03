using System.DirectoryServices.Protocols;
using Wec.Core.Results;

namespace Wec.Infrastructure.Directory;

/// <summary>
/// Turns the two S.DS.Protocols failure shapes (LdapException with a Win32
/// LDAP error code, DirectoryOperationException with an LDAP result code)
/// into diagnostically distinct typed errors instead of one generic
/// DIRECTORY_UNAVAILABLE.
/// </summary>
internal static class LdapErrorMapper
{
    private const int LdapInvalidCredentials = 49;
    private const int LdapServerDown = 81;
    private const int LdapLocalTimeout = 85;

    public static Error MapLdapException(int ldapErrorCode, string message, string target) => ldapErrorCode switch
    {
        LdapInvalidCredentials => new Error(
            ErrorCode.AuthenticationFailed,
            $"The LDAP bind against '{target}' was rejected.")
        {
            Details = "Wrong user name/password, expired password, or a locked account. " + message,
        },
        LdapServerDown => new Error(
            ErrorCode.DirectoryUnavailable,
            $"No LDAP server for '{target}' is reachable.")
        {
            Details = "No domain controller answered: the DC may be down, the DNS servers may not "
                + "know the domain, or a firewall blocks TCP 389/636. " + message,
        },
        LdapLocalTimeout => new Error(
            ErrorCode.ConnectionTimeout,
            $"The LDAP request against '{target}' timed out.")
        {
            Details = message,
        },
        _ => new Error(
            ErrorCode.DirectoryUnavailable,
            $"The directory for '{target}' could not be queried.")
        {
            Details = $"LDAP error {ldapErrorCode}: {message}",
        },
    };

    public static Error MapOperationResult(ResultCode? resultCode, string message, string target) => resultCode switch
    {
        ResultCode.InsufficientAccessRights => new Error(
            ErrorCode.AccessDenied,
            "The directory refused the read with the current credentials.")
        {
            Details = message,
        },
        ResultCode.NoSuchObject => new Error(
            ErrorCode.NotFound,
            "The search base does not exist in this directory (naming context missing).")
        {
            Details = message,
        },
        ResultCode.TimeLimitExceeded => new Error(
            ErrorCode.ConnectionTimeout,
            $"The directory did not answer within the time limit ('{target}').")
        {
            Details = message,
        },
        _ => new Error(
            ErrorCode.DirectoryUnavailable,
            $"The directory for '{target}' could not be queried.")
        {
            Details = message,
        },
    };
}
