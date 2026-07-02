using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.Inventory.Application;

namespace Wec.Modules.Inventory.Handlers;

public sealed record GetHardwareInfoRequest(bool ForceRefresh = false);

internal sealed class GetHardwareInfoHandler : IActionHandler<GetHardwareInfoRequest, HardwareInfoResult>
{
    private readonly HardwareInfoService _hardwareInfoService;

    public GetHardwareInfoHandler(HardwareInfoService hardwareInfoService)
    {
        _hardwareInfoService = hardwareInfoService;
    }

    public string Module => "inventory";

    public string Action => "getHardwareInfo";

    public Task<Result<HardwareInfoResult>> HandleAsync(
        GetHardwareInfoRequest payload,
        CancellationToken cancellationToken) =>
        _hardwareInfoService.GetHardwareInfoAsync(payload.ForceRefresh, cancellationToken);
}
