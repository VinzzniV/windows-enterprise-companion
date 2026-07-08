using Wec.Core.Ccrx;
using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.PrintManagement.Application;
using Wec.Modules.PrintManagement.Domain;

namespace Wec.Modules.PrintManagement.Handlers;

/// <summary>
/// One printer's notification-config check. <c>Host</c> is the device address
/// (IP or name). <c>SiteCode</c> feeds the expected-server <c>{site}</c> token.
/// Credentials are optional and session-only (never persisted/logged, ADR 0007);
/// omitted = the factory default Admin/Admin the fleet ships on.
/// </summary>
public sealed record CheckNotificationConfigRequest(
    string Host,
    string? SiteCode = null,
    string? UserName = null,
    string? Password = null);

internal sealed class CheckNotificationConfigHandler
    : IActionHandler<CheckNotificationConfigRequest, PrinterNotificationCheck>
{
    private readonly NotificationConfigService _service;

    public CheckNotificationConfigHandler(NotificationConfigService service)
    {
        _service = service;
    }

    public string Module => "printmanagement";

    public string Action => "checkNotificationConfig";

    public async Task<Result<PrinterNotificationCheck>> HandleAsync(
        CheckNotificationConfigRequest payload, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payload.Host))
        {
            return Result.Failure<PrinterNotificationCheck>(new Error(
                ErrorCode.InvalidRequest, "A device address is required to check its notification config."));
        }

        CcrxCredentials credentials = string.IsNullOrEmpty(payload.Password)
            ? CcrxCredentials.Default
            : new CcrxCredentials(
                string.IsNullOrWhiteSpace(payload.UserName) ? CcrxCredentials.Default.UserName : payload.UserName!,
                payload.Password!);

        PrinterNotificationCheck check = await _service.CheckAsync(
            payload.Host.Trim(), payload.SiteCode, credentials, cancellationToken);
        return Result.Success(check);
    }
}
