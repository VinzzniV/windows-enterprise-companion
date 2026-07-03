using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.Inventory.Application;

namespace Wec.Modules.Inventory.Handlers;

public sealed record GetHardwareInfoRequest(bool ForceRefresh = false, TargetRequest? Target = null);

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
        CancellationToken cancellationToken)
    {
        TargetRequest targetRequest = payload.Target ?? new TargetRequest();
        Result<ScanCredentials> credentials = targetRequest.ToScanCredentials();
        if (credentials.IsFailure)
        {
            return Task.FromResult(Result.Failure<HardwareInfoResult>(credentials.Error!));
        }

        return _hardwareInfoService.GetHardwareInfoAsync(
            targetRequest.ToScanTarget(),
            credentials.Value,
            payload.ForceRefresh,
            cancellationToken);
    }
}
