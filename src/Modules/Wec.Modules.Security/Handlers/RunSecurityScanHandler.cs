using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.Security.Application;
using Wec.Modules.Security.Domain;

namespace Wec.Modules.Security.Handlers;

public sealed record RunSecurityScanRequest;

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
        CancellationToken cancellationToken) =>
        _securityScanService.RunScanAsync(cancellationToken);
}
