using Microsoft.Extensions.Options;
using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.PrintManagement.Application;
using Wec.Modules.PrintManagement.Domain;
using Wec.Modules.PrintManagement.Persistence;

namespace Wec.Modules.PrintManagement.Handlers;

public sealed record ScanPrintServerRequest(TargetRequest? Target = null);

internal sealed class ScanPrintServerHandler : IActionHandler<ScanPrintServerRequest, PrintServerSnapshot>
{
    private readonly PrintServerScanService _scanService;
    private readonly IPrintSnapshotRepository _repository;
    private readonly PrintManagementOptions _options;

    public ScanPrintServerHandler(
        PrintServerScanService scanService,
        IPrintSnapshotRepository repository,
        IOptions<PrintManagementOptions> options)
    {
        _scanService = scanService;
        _repository = repository;
        _options = options.Value;
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

        Result<PrintServerSnapshot> snapshot = await _scanService.CaptureAsync(
            target.ToScanTarget(), credentials.Value, cancellationToken);
        if (snapshot.IsSuccess)
        {
            await _repository.SaveAsync(snapshot.Value, _options.HistoryLimit, cancellationToken);
        }

        return snapshot;
    }
}
