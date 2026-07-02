using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.Diagnostics.Application;
using Wec.Modules.Diagnostics.Domain;

namespace Wec.Modules.Diagnostics.Handlers;

public sealed record RunDiagnosticsRequest;

internal sealed class RunDiagnosticsHandler : IActionHandler<RunDiagnosticsRequest, DiagnosticRunResult>
{
    private readonly DiagnosticRunService _diagnosticRunService;

    public RunDiagnosticsHandler(DiagnosticRunService diagnosticRunService)
    {
        _diagnosticRunService = diagnosticRunService;
    }

    public string Module => "diagnostics";

    public string Action => "runDiagnostics";

    public Task<Result<DiagnosticRunResult>> HandleAsync(
        RunDiagnosticsRequest payload,
        CancellationToken cancellationToken) =>
        _diagnosticRunService.RunAsync(cancellationToken);
}
