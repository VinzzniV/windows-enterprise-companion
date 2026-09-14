using Wec.Core.Messaging;
using Wec.Core.Microsoft365;
using Wec.Core.Results;
using Wec.Modules.Microsoft365.Application;
using Wec.Modules.Microsoft365.Domain;

namespace Wec.Modules.Microsoft365.Handlers;

public sealed record Microsoft365EmptyRequest;
public sealed record Microsoft365ReadRequest(Microsoft365Resource Resource, string? ObjectId = null, bool Refresh = false);
public sealed record Microsoft365ContextRequest(string? Sid = null, string? UserPrincipalName = null,
    string? Host = null, string? EntraDeviceId = null);

internal sealed class Microsoft365StatusHandler(Microsoft365Service service) : IActionHandler<Microsoft365EmptyRequest, Microsoft365Status>
{
    public string Module => "microsoft365";
    public string Action => "getStatus";
    public async Task<Result<Microsoft365Status>> HandleAsync(Microsoft365EmptyRequest payload, CancellationToken cancellationToken) =>
        Result.Success(await service.StatusAsync(cancellationToken).ConfigureAwait(false));
}
internal sealed class Microsoft365ConnectHandler(Microsoft365Service service) : IActionHandler<Microsoft365Configuration, Microsoft365Connection>
{
    public string Module => "microsoft365";
    public string Action => "connect";
    public Task<Result<Microsoft365Connection>> HandleAsync(Microsoft365Configuration payload, CancellationToken cancellationToken) =>
        service.ConnectAsync(payload, cancellationToken);
}
internal sealed class Microsoft365DisconnectHandler(Microsoft365Service service) : IActionHandler<Microsoft365EmptyRequest, bool>
{
    public string Module => "microsoft365";
    public string Action => "disconnect";
    public Task<Result<bool>> HandleAsync(Microsoft365EmptyRequest payload, CancellationToken cancellationToken) => service.DisconnectAsync(cancellationToken);
}
internal sealed class Microsoft365ReadHandler(Microsoft365Service service) : IActionHandler<Microsoft365ReadRequest, Microsoft365Snapshot>
{
    public string Module => "microsoft365";
    public string Action => "read";
    public Task<Result<Microsoft365Snapshot>> HandleAsync(Microsoft365ReadRequest payload, CancellationToken cancellationToken) =>
        service.ReadAsync(new(payload.Resource, payload.ObjectId), payload.Refresh, cancellationToken);
}
internal sealed class Microsoft365ContextHandler(Microsoft365Service service) : IActionHandler<Microsoft365ContextRequest, Microsoft365Correlation>
{
    public string Module => "microsoft365";
    public string Action => "getContext";
    public async Task<Result<Microsoft365Correlation>> HandleAsync(Microsoft365ContextRequest payload, CancellationToken cancellationToken) =>
        Result.Success(await service.ContextAsync(payload.Sid, payload.UserPrincipalName, payload.Host, payload.EntraDeviceId, cancellationToken).ConfigureAwait(false));
}
