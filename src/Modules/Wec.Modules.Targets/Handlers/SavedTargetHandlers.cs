using Wec.Core.Abstractions;
using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.Targets.Persistence;

namespace Wec.Modules.Targets.Handlers;

public sealed record ListSavedTargetsRequest;

public sealed record SavedTargetsResult(IReadOnlyList<SavedTarget> Targets);

internal sealed class ListSavedTargetsHandler : IActionHandler<ListSavedTargetsRequest, SavedTargetsResult>
{
    private readonly ISavedTargetRepository _repository;

    public ListSavedTargetsHandler(ISavedTargetRepository repository)
    {
        _repository = repository;
    }

    public string Module => "targets";

    public string Action => "list";

    public async Task<Result<SavedTargetsResult>> HandleAsync(
        ListSavedTargetsRequest payload, CancellationToken cancellationToken) =>
        Result.Success(new SavedTargetsResult(await _repository.ListAsync(cancellationToken)));
}

public sealed record SaveTargetRequest(string Label, string Host, string Role, string? UserName);

internal sealed class SaveTargetHandler : IActionHandler<SaveTargetRequest, SavedTargetsResult>
{
    private readonly ISavedTargetRepository _repository;
    private readonly IClock _clock;

    public SaveTargetHandler(ISavedTargetRepository repository, IClock clock)
    {
        _repository = repository;
        _clock = clock;
    }

    public string Module => "targets";

    public string Action => "save";

    public async Task<Result<SavedTargetsResult>> HandleAsync(
        SaveTargetRequest payload, CancellationToken cancellationToken)
    {
        string host = payload.Host?.Trim() ?? string.Empty;
        string role = payload.Role?.Trim() ?? string.Empty;
        if (host.Length == 0)
        {
            return Result.Failure<SavedTargetsResult>(new Error(
                ErrorCode.InvalidRequest, "A host is required to save a target."));
        }

        if (!TargetRoles.IsKnown(role))
        {
            return Result.Failure<SavedTargetsResult>(new Error(
                ErrorCode.InvalidRequest, $"Unknown target role '{payload.Role}'."));
        }

        string label = payload.Label?.Trim() is { Length: > 0 } trimmed ? trimmed : host;
        string? userName = payload.UserName?.Trim() is { Length: > 0 } user ? user : null;

        await _repository.UpsertAsync(label, host, role, userName, _clock.UtcNow, cancellationToken);
        return Result.Success(new SavedTargetsResult(await _repository.ListAsync(cancellationToken)));
    }
}

public sealed record DeleteSavedTargetRequest(int Id);

internal sealed class DeleteSavedTargetHandler : IActionHandler<DeleteSavedTargetRequest, SavedTargetsResult>
{
    private readonly ISavedTargetRepository _repository;

    public DeleteSavedTargetHandler(ISavedTargetRepository repository)
    {
        _repository = repository;
    }

    public string Module => "targets";

    public string Action => "delete";

    public async Task<Result<SavedTargetsResult>> HandleAsync(
        DeleteSavedTargetRequest payload, CancellationToken cancellationToken)
    {
        await _repository.DeleteAsync(payload.Id, cancellationToken);
        return Result.Success(new SavedTargetsResult(await _repository.ListAsync(cancellationToken)));
    }
}
