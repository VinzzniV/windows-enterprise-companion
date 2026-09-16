using Wec.Core.Messaging;
using Wec.Core.Objects;
using Wec.Core.Results;
using Wec.Modules.Clients.Application;

namespace Wec.Modules.Clients.Handlers;

public sealed record DeviceWorkspaceRequest;

internal sealed class DeviceWorkspaceHandler(WecWorkspaceIdentity workspace) : IActionHandler<DeviceWorkspaceRequest, WecWorkspaceIdentity>
{
    public string Module => "clients";
    public string Action => "getWorkspace";
    public Task<Result<WecWorkspaceIdentity>> HandleAsync(DeviceWorkspaceRequest payload, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success(workspace));
}

internal sealed class DeviceProfileHandler(DeviceProfileService service) : IActionHandler<DeviceProfileRequest, DeviceProfileResult>
{
    public string Module => "clients";
    public string Action => "getProfile";
    public Task<Result<DeviceProfileResult>> HandleAsync(DeviceProfileRequest payload, CancellationToken cancellationToken) =>
        service.GetAsync(payload, cancellationToken);
}
