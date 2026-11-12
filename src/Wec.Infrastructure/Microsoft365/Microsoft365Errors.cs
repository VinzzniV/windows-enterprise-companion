using System.Net.Http;
using System.Net.NetworkInformation;
using Microsoft.Identity.Client;
using Microsoft.Kiota.Abstractions;
using Wec.Core.Results;

namespace Wec.Infrastructure.Microsoft365;

internal static class Microsoft365Errors
{
    internal static Error From(Exception exception) => exception switch
    {
        MsalException { ErrorCode: "wec_scope_mismatch" } => new(ErrorCode.Microsoft365ConfigurationInvalid,
            "The Microsoft 365 token contains permissions outside WEC's selected read profile. Use a dedicated app registration with only the documented scopes and review its existing consent."),
        MsalUiRequiredException { Classification: UiRequiredExceptionClassification.ConsentRequired } =>
            new(ErrorCode.Microsoft365ConsentRequired, "Microsoft 365 consent is missing. Ask a tenant administrator to grant the selected read permissions, then sign in again."),
        MsalException { ErrorCode: "invalid_client" or "unauthorized_client" or "invalid_tenant" } =>
            new(ErrorCode.Microsoft365ConfigurationInvalid, "Check the tenant ID, public-client app registration and WAM redirect URI."),
        MsalServiceException { ErrorCode: "consent_required" } =>
            new(ErrorCode.Microsoft365ConsentRequired, "An administrator must grant consent for the selected Microsoft 365 permissions."),
        MsalUiRequiredException => new(ErrorCode.Microsoft365AuthenticationRequired,
            "Microsoft 365 requires a fresh sign-in. The token may have expired or tenant policy requires interaction. Select Sign in."),
        MsalException => new(ErrorCode.Microsoft365AuthenticationRequired,
            "Microsoft 365 sign-in was not completed. Check the Windows account broker, app registration and tenant access policy, then select Sign in."),
        ApiException api => FromStatus(api.ResponseStatusCode),
        HttpRequestException => NetworkInterface.GetIsNetworkAvailable()
            ? new(ErrorCode.Microsoft365Unavailable, "Microsoft Graph could not be reached. Check DNS, proxy, TLS and service availability; an active network adapter does not prove Internet connectivity.")
            : new(ErrorCode.Microsoft365Offline, "Windows reports no available network connection. Connect to the network and refresh."),
        OperationCanceledException => new(ErrorCode.Microsoft365Timeout, "The Microsoft 365 request exceeded its time limit. Retry after checking connectivity."),
        _ => new(ErrorCode.Microsoft365InvalidResponse, "Microsoft Graph returned an invalid or unsupported response. Refresh or check the WEC error log."),
    };

    internal static Error FromStatus(int status) => status switch
    {
        401 => new(ErrorCode.Microsoft365AuthenticationRequired, "Microsoft Graph rejected the session. Sign in again; the token may have expired or been revoked."),
        403 => new(ErrorCode.Microsoft365AccessDenied, "Microsoft Graph denied this read. Verify the displayed permission, admin consent, the signed-in user's Entra role, Conditional Access and any required Intune or Entra license."),
        404 => new(ErrorCode.Microsoft365NotFound, "This Microsoft 365 object or report is not available. It may have been deleted or may not exist for this account."),
        429 => new(ErrorCode.Microsoft365Throttled, "Microsoft Graph is throttling requests. Automatic retries are exhausted or Retry-After exceeds the wait budget. Wait before refreshing."),
        400 => new(ErrorCode.Microsoft365Unsupported, "Microsoft Graph rejected this query. This property or function may be unsupported for the tenant or account."),
        405 or 501 => new(ErrorCode.Microsoft365Unsupported, "This Microsoft Graph function is not supported by the service."),
        >= 500 => new(ErrorCode.Microsoft365Unavailable, "Microsoft Graph is temporarily unavailable. Retry later."),
        _ => new(ErrorCode.Microsoft365InvalidResponse, "Microsoft Graph returned an unexpected response. Refresh or check the WEC error log."),
    };
}
