using Wec.Core.Results;

namespace Wec.Core.Abstractions;

public interface IWmiQueryService
{
    Task<Result<IReadOnlyList<WmiInstance>>> QueryAsync(
        string wmiNamespace,
        string wqlQuery,
        CancellationToken cancellationToken);
}
