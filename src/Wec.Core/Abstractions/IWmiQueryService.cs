using Wec.Core.Results;
using Wec.Core.Targets;

namespace Wec.Core.Abstractions;

public interface IWmiQueryService
{
    Task<Result<IReadOnlyList<WmiInstance>>> QueryAsync(
        ScanTarget target,
        ScanCredentials credentials,
        ConnectionOptions connection,
        string wmiNamespace,
        string wqlQuery,
        CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<WmiInstance>>> QueryAsync(
        string wmiNamespace,
        string wqlQuery,
        CancellationToken cancellationToken) =>
        QueryAsync(ScanTarget.Local, ScanCredentials.CurrentUser, ConnectionOptions.Default, wmiNamespace, wqlQuery, cancellationToken);
}
