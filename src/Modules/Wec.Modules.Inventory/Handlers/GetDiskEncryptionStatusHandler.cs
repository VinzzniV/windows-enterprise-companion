using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.Inventory.Application;

namespace Wec.Modules.Inventory.Handlers;

public sealed record GetDiskEncryptionStatusRequest(TargetRequest? Target = null);

internal sealed class GetDiskEncryptionStatusHandler
    : IActionHandler<GetDiskEncryptionStatusRequest, DiskEncryptionStatus>
{
    private readonly DiskEncryptionService _diskEncryptionService;

    public GetDiskEncryptionStatusHandler(DiskEncryptionService diskEncryptionService)
    {
        _diskEncryptionService = diskEncryptionService;
    }

    public string Module => "inventory";

    public string Action => "getDiskEncryptionStatus";

    public Task<Result<DiskEncryptionStatus>> HandleAsync(
        GetDiskEncryptionStatusRequest payload,
        CancellationToken cancellationToken)
    {
        TargetRequest targetRequest = payload.Target ?? new TargetRequest();
        Result<ScanCredentials> credentials = targetRequest.ToScanCredentials();
        if (credentials.IsFailure)
        {
            return Task.FromResult(Result.Failure<DiskEncryptionStatus>(credentials.Error!));
        }

        return _diskEncryptionService.GetStatusAsync(
            targetRequest.ToScanTarget(),
            credentials.Value,
            cancellationToken);
    }
}
