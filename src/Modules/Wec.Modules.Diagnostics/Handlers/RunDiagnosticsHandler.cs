using Microsoft.Extensions.Options;
using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.Diagnostics.Application;
using Wec.Modules.Diagnostics.Domain;

namespace Wec.Modules.Diagnostics.Handlers;

public sealed record RunDiagnosticsRequest(TargetRequest? Target = null);

internal sealed class RunDiagnosticsHandler : IActionHandler<RunDiagnosticsRequest, DiagnosticRunResult>
{
    private readonly DiagnosticRunService _diagnosticRunService;
    private readonly RemoteScanOptions _remoteScanOptions;

    public RunDiagnosticsHandler(
        DiagnosticRunService diagnosticRunService,
        IOptions<RemoteScanOptions> remoteScanOptions)
    {
        _diagnosticRunService = diagnosticRunService;
        _remoteScanOptions = remoteScanOptions.Value;
    }

    public string Module => "diagnostics";

    public string Action => "runDiagnostics";

    public Task<Result<DiagnosticRunResult>> HandleAsync(
        RunDiagnosticsRequest payload,
        CancellationToken cancellationToken)
    {
        TargetRequest targetRequest = payload.Target ?? new TargetRequest();
        Result<ScanCredentials> credentials = targetRequest.ToScanCredentials();
        if (credentials.IsFailure)
        {
            return Task.FromResult(Result.Failure<DiagnosticRunResult>(credentials.Error!));
        }

        var context = new DiagnosticContext(
            targetRequest.ToScanTarget(),
            credentials.Value,
            _remoteScanOptions.ToConnectionOptions());
        return _diagnosticRunService.RunAsync(context, cancellationToken);
    }
}
