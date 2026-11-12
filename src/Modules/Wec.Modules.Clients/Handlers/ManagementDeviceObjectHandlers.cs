using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.Clients.Application;

namespace Wec.Modules.Clients.Handlers;

internal sealed class ManagementDeviceObjectListsHandler(ManagementDeviceObjectService service)
    : IActionHandler<ManagementDeviceListRequest, ManagementDeviceObjectLists>
{
    public string Module => "clients";
    public string Action => "getCachedManagementObjectLists";
    public Task<Result<ManagementDeviceObjectLists>> HandleAsync(ManagementDeviceListRequest payload, CancellationToken cancellationToken) =>
        service.ListCachedAsync(payload, cancellationToken);
}

internal sealed class ManagementDeviceRecordHandler(ManagementDeviceObjectService service)
    : IActionHandler<ManagementDeviceRecordRequest, ManagementDeviceRecordProfile>
{
    public string Module => "clients";
    public string Action => "getManagementRecord";
    public Task<Result<ManagementDeviceRecordProfile>> HandleAsync(ManagementDeviceRecordRequest payload, CancellationToken cancellationToken) =>
        service.ReadCachedAsync(payload, cancellationToken);
}
