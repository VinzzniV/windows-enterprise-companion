using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.Security.Application;

namespace Wec.Modules.Security.Handlers;

public sealed record GetLatestSecurityScanRequest(TargetRequest? Target = null);

internal sealed class GetLatestSecurityScanHandler : IActionHandler<GetLatestSecurityScanRequest, LatestScanResult>
{
    private readonly SecurityScanService _securityScanService;

    public GetLatestSecurityScanHandler(SecurityScanService securityScanService)
    {
        _securityScanService = securityScanService;
    }

    public string Module => "security";

    public string Action => "getLatestScan";

    public Task<Result<LatestScanResult>> HandleAsync(
        GetLatestSecurityScanRequest payload,
        CancellationToken cancellationToken) =>
        _securityScanService.GetLatestScanAsync(
            (payload.Target ?? new TargetRequest()).ToScanTarget(),
            cancellationToken);
}
