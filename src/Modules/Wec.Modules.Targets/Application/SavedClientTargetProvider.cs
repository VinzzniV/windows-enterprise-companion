using Wec.Core.Contracts;
using Wec.Modules.Targets.Persistence;

namespace Wec.Modules.Targets.Application;

internal sealed class SavedClientTargetProvider(
    ISavedTargetRepository repository) : ISavedClientTargetProvider
{
    public async Task<IReadOnlyList<SavedClientTarget>> ListClientsAsync(
        CancellationToken cancellationToken) =>
        (await repository.ListAsync(cancellationToken))
            .Where(target => target.Role == TargetRoles.Client)
            .Select(target => new SavedClientTarget(target.Label, target.Host))
            .ToList();
}
