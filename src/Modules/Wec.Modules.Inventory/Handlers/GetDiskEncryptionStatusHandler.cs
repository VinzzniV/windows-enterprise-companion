using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.Inventory.Application;

namespace Wec.Modules.Inventory.Handlers;

public sealed record GetDiskEncryptionStatusRequest;

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
        CancellationToken cancellationToken) =>
        _diskEncryptionService.GetStatusAsync(cancellationToken);
}
