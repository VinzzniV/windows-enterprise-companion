using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.Security.Application;
using Wec.Modules.Security.Domain;

namespace Wec.Modules.Security.Handlers;

public sealed record RunSecurityScanRequest(TargetRequest? Target = null);

internal sealed class RunSecurityScanHandler : IActionHandler<RunSecurityScanRequest, SecurityScanResult>
{
    private readonly SecurityScanService _securityScanService;

    public RunSecurityScanHandler(SecurityScanService securityScanService)
    {
        _securityScanService = securityScanService;
    }

    public string Module => "security";

    public string Action => "runScan";

    public Task<Result<SecurityScanResult>> HandleAsync(
        RunSecurityScanRequest payload,
        CancellationToken cancellationToken)
    {
        TargetRequest targetRequest = payload.Target ?? new TargetRequest();
        Result<ScanCredentials> credentials = targetRequest.ToScanCredentials();
        if (credentials.IsFailure)
        {
            return Task.FromResult(Result.Failure<SecurityScanResult>(credentials.Error!));
        }

        return _securityScanService.RunScanAsync(
            targetRequest.ToScanTarget(),
            credentials.Value,
            cancellationToken);
    }
}
