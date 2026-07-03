using Microsoft.Management.Infrastructure;
using Wec.Core.Privileges;
using Wec.Core.Results;

namespace Wec.Infrastructure.Wmi;

/// <summary>
/// Maps CIM/WinRM failures onto the typed error model. WSMan reports its
/// detailed HRESULT in the error instance attached to the exception; the
/// native MI result alone cannot distinguish transport failures.
/// </summary>
internal static class RemoteCimErrorMapper
{
    private const uint WsmanAccessDenied = 0x80070005;
    private const uint WsmanClientCannotConnect = 0x80338012;
    private const uint WsmanOperationTimedOut = 0x80338029;
    private const uint WsmanConnectTimedOut = 0x80338126;
    private const uint WsmanInvalidAuthentication = 0x80338043;
    private const uint WsmanLogonFailure = 0x8033809D;

    public static Error Map(CimException exception, bool isRemote, string targetDisplayName)
    {
        uint? wsmanErrorCode = ExtractWsmanErrorCode(exception);
        return Map(exception.NativeErrorCode, wsmanErrorCode, exception.Message, isRemote, targetDisplayName);
    }

    public static Error Map(
        NativeErrorCode nativeErrorCode,
        uint? wsmanErrorCode,
        string message,
        bool isRemote,
        string targetDisplayName)
    {
        if (wsmanErrorCode is WsmanAccessDenied or WsmanInvalidAuthentication or WsmanLogonFailure
            || nativeErrorCode == NativeErrorCode.AccessDenied)
        {
            return isRemote
                ? new Error(
                    ErrorCode.AuthenticationFailed,
                    $"Authentication against '{targetDisplayName}' failed.")
                {
                    Details = "The credentials were rejected, or the account lacks remote management rights. " + message,
                }
                : Error.AccessDenied(
                    "Access to WMI was denied.",
                    PrivilegeLevel.Administrator);
        }

        if (wsmanErrorCode is WsmanOperationTimedOut or WsmanConnectTimedOut)
        {
            return new Error(
                ErrorCode.ConnectionTimeout,
                $"The connection to '{targetDisplayName}' timed out.")
            {
                Details = message,
            };
        }

        if (wsmanErrorCode is WsmanClientCannotConnect)
        {
            return new Error(
                ErrorCode.WinRmUnavailable,
                $"WinRM on '{targetDisplayName}' is not reachable.")
            {
                Details = "The WinRM service is not running, or a firewall blocks TCP 5985/5986. " + message,
            };
        }

        return isRemote
            ? new Error(
                ErrorCode.WinRmUnavailable,
                $"The remote CIM query against '{targetDisplayName}' failed.")
            {
                Details = message,
            }
            : Error.WmiUnavailable("The WMI query failed.", message);
    }

    private static uint? ExtractWsmanErrorCode(CimException exception)
    {
        object? errorCode = exception.ErrorData?
            .CimInstanceProperties["error_Code"]?
            .Value;
        return errorCode switch
        {
            uint code => code,
            int code => unchecked((uint)code),
            _ => null,
        };
    }
}
