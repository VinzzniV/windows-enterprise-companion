using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.Security.Application;
using Wec.Modules.Security.Domain;

namespace Wec.Modules.Security.Handlers;

public sealed record GetScanHistoryRequest;

internal sealed class GetScanHistoryHandler : IActionHandler<GetScanHistoryRequest, ScanHistoryResult>
{
    private readonly ScanHistoryService _scanHistoryService;

    public GetScanHistoryHandler(ScanHistoryService scanHistoryService)
    {
        _scanHistoryService = scanHistoryService;
    }

    public string Module => "security";

    public string Action => "getScanHistory";

    public Task<Result<ScanHistoryResult>> HandleAsync(
        GetScanHistoryRequest payload,
        CancellationToken cancellationToken) =>
        _scanHistoryService.GetHistoryAsync(cancellationToken);
}
