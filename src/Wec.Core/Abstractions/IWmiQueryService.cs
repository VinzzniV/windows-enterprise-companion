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

    /// <summary>
    /// Invokes a static WMI class method (e.g. StdRegProv registry reads) and
    /// returns the out-parameters plus <c>ReturnValue</c> as a property bag.
    /// </summary>
    Task<Result<WmiInstance>> InvokeMethodAsync(
        ScanTarget target,
        ScanCredentials credentials,
        ConnectionOptions connection,
        string wmiNamespace,
        string className,
        string methodName,
        IReadOnlyDictionary<string, object?> inputParameters,
        CancellationToken cancellationToken);
}
