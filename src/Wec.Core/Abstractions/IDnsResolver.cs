using Wec.Core.Results;

namespace Wec.Core.Abstractions;

public interface IDnsResolver
{
    Task<Result<IReadOnlyList<string>>> ResolveAsync(string hostname, CancellationToken cancellationToken);
}
