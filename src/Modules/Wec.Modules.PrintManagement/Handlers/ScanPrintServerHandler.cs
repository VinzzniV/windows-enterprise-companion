using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.PrintManagement.Application;
using Wec.Modules.PrintManagement.Domain;

namespace Wec.Modules.PrintManagement.Handlers;

public sealed record ScanPrintServerRequest(TargetRequest? Target = null);

internal sealed class ScanPrintServerHandler : IActionHandler<ScanPrintServerRequest, PrintServerSnapshot>
{
    private readonly PrintServerScanService _scanService;

    public ScanPrintServerHandler(PrintServerScanService scanService)
    {
        _scanService = scanService;
    }

    public string Module => "printmanagement";

    public string Action => "scanServer";

    public async Task<Result<PrintServerSnapshot>> HandleAsync(
        ScanPrintServerRequest payload, CancellationToken cancellationToken)
    {
        TargetRequest target = payload.Target ?? new TargetRequest();
        Result<ScanCredentials> credentials = target.ToScanCredentials();
        if (credentials.IsFailure)
        {
            return Result.Failure<PrintServerSnapshot>(credentials.Error!);
        }

        return await _scanService.CaptureAsync(
            target.ToScanTarget(), credentials.Value, cancellationToken);
    }
}
