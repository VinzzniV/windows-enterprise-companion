using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.PrintManagement.Application;
using Wec.Modules.PrintManagement.Domain;
using Wec.Modules.PrintManagement.Persistence;

namespace Wec.Modules.PrintManagement.Handlers;

public sealed record ScanClientPrintersRequest(TargetRequest? Target = null);

internal sealed class ScanClientPrintersHandler : IActionHandler<ScanClientPrintersRequest, ClientPrinterScan>
{
    private readonly ClientPrinterScanService _scanService;
    private readonly IClientPrinterScanRepository _repository;

    public ScanClientPrintersHandler(
        ClientPrinterScanService scanService, IClientPrinterScanRepository repository)
    {
        _scanService = scanService;
        _repository = repository;
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

        Result<ClientPrinterScan> scan = await _scanService.CaptureAsync(
            target.ToScanTarget(), credentials.Value, cancellationToken);
        if (scan.IsSuccess)
        {
            // Always persist the latest scan so it is there on the next open.
            await _repository.SaveAsync(scan.Value, cancellationToken);
        }

        return scan;
    }
}

public sealed record GetLatestClientPrintersRequest(TargetRequest? Target = null);

public sealed record LatestClientPrinterScanResult(ClientPrinterScan? Scan);

internal sealed class GetLatestClientPrintersHandler
    : IActionHandler<GetLatestClientPrintersRequest, LatestClientPrinterScanResult>
{
    private readonly IClientPrinterScanRepository _repository;

    public GetLatestClientPrintersHandler(IClientPrinterScanRepository repository)
    {
        _repository = repository;
    }

    public string Module => "printmanagement";

    public string Action => "getLatestClientPrinters";

    public async Task<Result<LatestClientPrinterScanResult>> HandleAsync(
        GetLatestClientPrintersRequest payload, CancellationToken cancellationToken)
    {
        TargetRequest target = payload.Target ?? new TargetRequest();
        ClientPrinterScan? scan = await _repository.GetLatestAsync(
            target.ToScanTarget().CacheKey, cancellationToken);
        return Result.Success(new LatestClientPrinterScanResult(scan));
    }
}
