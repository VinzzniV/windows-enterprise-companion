using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.Diagnostics.Application;
using Wec.Modules.Diagnostics.Domain;

namespace Wec.Modules.Diagnostics.Handlers;

public sealed record RunBatchDiagnosticsRequest(
    IReadOnlyList<string>? Hosts = null,
    string? UserName = null,
    string? Domain = null,
    string? Password = null);

internal sealed class RunBatchDiagnosticsHandler
    : IActionHandler<RunBatchDiagnosticsRequest, DiagnosticBatchResult>
{
    private readonly BatchDiagnosticService _batchDiagnosticService;

    public RunBatchDiagnosticsHandler(BatchDiagnosticService batchDiagnosticService)
    {
        _batchDiagnosticService = batchDiagnosticService;
    }

    public string Module => "diagnostics";

    public string Action => "runBatchDiagnostics";

    public Task<Result<DiagnosticBatchResult>> HandleAsync(
        RunBatchDiagnosticsRequest payload,
        CancellationToken cancellationToken)
    {
        ScanCredentials credentials;
        if (string.IsNullOrWhiteSpace(payload.UserName))
        {
            credentials = ScanCredentials.CurrentUser;
        }
        else if (payload.Password is null)
        {
            return Task.FromResult(Result.Failure<DiagnosticBatchResult>(new Error(
                ErrorCode.InvalidRequest,
                "Explicit credentials require a password.")));
        }
        else
        {
            credentials = ScanCredentials.Explicit(payload.UserName, payload.Domain, payload.Password);
        }

        return _batchDiagnosticService.RunAsync(
            payload.Hosts ?? [],
            credentials,
            cancellationToken);
    }
}
