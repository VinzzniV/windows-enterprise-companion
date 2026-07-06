using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.PrintManagement.Application;
using Wec.Modules.PrintManagement.Domain;

namespace Wec.Modules.PrintManagement.Handlers;

public sealed record ScanClientPrintersRequest(TargetRequest? Target = null);

internal sealed class ScanClientPrintersHandler : IActionHandler<ScanClientPrintersRequest, ClientPrinterScan>
{
    private readonly ClientPrinterScanService _scanService;

    public ScanClientPrintersHandler(ClientPrinterScanService scanService)
    {
        _scanService = scanService;
    }

    public string Module => "printmanagement";

    public string Action => "scanClientPrinters";

    public async Task<Result<ClientPrinterScan>> HandleAsync(
        ScanClientPrintersRequest payload, CancellationToken cancellationToken)
    {
        TargetRequest target = payload.Target ?? new TargetRequest();
        Result<ScanCredentials> credentials = target.ToScanCredentials();
        if (credentials.IsFailure)
        {
            return Result.Failure<ClientPrinterScan>(credentials.Error!);
        }

        return await _scanService.CaptureAsync(target.ToScanTarget(), credentials.Value, cancellationToken);
    }
}
