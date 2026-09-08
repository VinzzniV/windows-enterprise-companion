using Microsoft.Extensions.Options;
using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.Diagnostics.Application;
using Wec.Modules.Diagnostics.Domain;
using Wec.Modules.Diagnostics.Persistence;

namespace Wec.Modules.Diagnostics.Handlers;

public sealed record RunDiagnosticsRequest(TargetRequest? Target = null);

internal sealed class RunDiagnosticsHandler : IActionHandler<RunDiagnosticsRequest, DiagnosticRunResult>
{
    private readonly DiagnosticRunService _diagnosticRunService;
    private readonly IDiagnosticRunRepository _repository;
    private readonly RemoteScanOptions _remoteScanOptions;

    public RunDiagnosticsHandler(
        DiagnosticRunService diagnosticRunService,
        IDiagnosticRunRepository repository,
        IOptions<RemoteScanOptions> remoteScanOptions)
    {
        _diagnosticRunService = diagnosticRunService;
        _repository = repository;
        _remoteScanOptions = remoteScanOptions.Value;
    }

    public string Module => "diagnostics";

    public string Action => "runDiagnostics";

    public async Task<Result<DiagnosticRunResult>> HandleAsync(
        RunDiagnosticsRequest payload,
        CancellationToken cancellationToken)
    {
        TargetRequest targetRequest = payload.Target ?? new TargetRequest();
        Result<ScanCredentials> credentials = targetRequest.ToScanCredentials();
        if (credentials.IsFailure)
        {
            return Result.Failure<DiagnosticRunResult>(credentials.Error!);
        }

        ScanTarget target = targetRequest.ToScanTarget();
        var context = new DiagnosticContext(
            target,
            credentials.Value,
            _remoteScanOptions.ToConnectionOptions());

        Result<DiagnosticRunResult> run = await _diagnosticRunService.RunAsync(context, cancellationToken);
        if (run.IsSuccess)
        {
            // Always persist the latest run so it is there on the next open.
            await _repository.SaveAsync(target.CacheKey, run.Value, cancellationToken);
        }

        return run;
    }
}

public sealed record GetLatestDiagnosticsRequest(TargetRequest? Target = null);

public sealed record LatestDiagnosticRunResult(DiagnosticRunResult? Run);

internal sealed class GetLatestDiagnosticsHandler
    : IActionHandler<GetLatestDiagnosticsRequest, LatestDiagnosticRunResult>
{
    private readonly IDiagnosticRunRepository _repository;

    public GetLatestDiagnosticsHandler(IDiagnosticRunRepository repository)
    {
        _repository = repository;
    }

    public string Module => "diagnostics";

    public string Action => "getLatestDiagnostics";

    public async Task<Result<LatestDiagnosticRunResult>> HandleAsync(
        GetLatestDiagnosticsRequest payload, CancellationToken cancellationToken)
    {
        TargetRequest target = payload.Target ?? new TargetRequest();
        try
        {
            DiagnosticRunResult? run = await _repository.GetLatestAsync(
                target.ToScanTarget().CacheKey, cancellationToken);
            return Result.Success(new LatestDiagnosticRunResult(run));
        }
        catch (InvalidDataException)
        {
            return Result.Failure<LatestDiagnosticRunResult>(new Error(
                ErrorCode.StoredDataUnreadable,
                "The stored health snapshot is unreadable."));
        }
    }
}
