using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Core.Targets;

namespace Wec.Modules.Security.Application;

public sealed record SecurityScanContext(
    ScanTarget Target,
    ScanCredentials Credentials,
    ConnectionOptions Connection);

internal static class SecurityScanContextWmiExtensions
{
    public static Task<Result<IReadOnlyList<WmiInstance>>> QueryAsync(
        this IWmiQueryService wmiQueryService,
        SecurityScanContext context,
        string wmiNamespace,
        string wqlQuery,
        CancellationToken cancellationToken) =>
        wmiQueryService.QueryAsync(
            context.Target,
            context.Credentials,
            context.Connection,
            wmiNamespace,
            wqlQuery,
            cancellationToken);
}
