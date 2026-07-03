using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.Security.Application;
using Wec.Modules.Security.Domain;

namespace Wec.Modules.Security.Handlers;

public sealed record RunBatchSecurityScanRequest(
    IReadOnlyList<string>? Hosts = null,
    string? UserName = null,
    string? Domain = null,
    string? Password = null);

internal sealed class RunBatchSecurityScanHandler
    : IActionHandler<RunBatchSecurityScanRequest, BatchScanResult>
{
    private readonly BatchSecurityScanService _batchScanService;

    public RunBatchSecurityScanHandler(BatchSecurityScanService batchScanService)
    {
        _batchScanService = batchScanService;
    }

    public string Module => "security";

    public string Action => "runBatchScan";

    public Task<Result<BatchScanResult>> HandleAsync(
        RunBatchSecurityScanRequest payload,
        CancellationToken cancellationToken)
    {
        ScanCredentials credentials;
        if (string.IsNullOrWhiteSpace(payload.UserName))
        {
            credentials = ScanCredentials.CurrentUser;
        }
        else if (payload.Password is null)
        {
            return Task.FromResult(Result.Failure<BatchScanResult>(new Error(
                ErrorCode.InvalidRequest,
                "Explicit credentials require a password.")));
        }
        else
        {
            credentials = ScanCredentials.Explicit(payload.UserName, payload.Domain, payload.Password);
        }

        return _batchScanService.RunBatchScanAsync(
            payload.Hosts ?? [],
            credentials,
            cancellationToken);
    }
}
