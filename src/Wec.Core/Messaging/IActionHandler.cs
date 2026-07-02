using Wec.Core.Results;

namespace Wec.Core.Messaging;

public interface IActionHandler
{
    string Module { get; }

    string Action { get; }
}

public interface IActionHandler<in TPayload, TResult> : IActionHandler
{
    Task<Result<TResult>> HandleAsync(TPayload payload, CancellationToken cancellationToken);
}
