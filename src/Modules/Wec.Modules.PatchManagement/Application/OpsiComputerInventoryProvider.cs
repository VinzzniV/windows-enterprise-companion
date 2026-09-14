using Wec.Core.Contracts;
using Wec.Core.Opsi;
using Wec.Core.Results;

namespace Wec.Modules.PatchManagement.Application;

/// <summary>Projects the active read-only opsi session into environment inventory data.</summary>
internal sealed class OpsiComputerInventoryProvider : IOpsiComputerInventoryProvider
{
    private const string ClientAgentProductId = "opsi-client-agent";

    private readonly IOpsiClient _client;
    private readonly OpsiSessionState _session;
    private readonly OpsiSessionConnector _connector;

    public OpsiComputerInventoryProvider(
        IOpsiClient client,
        OpsiSessionState session,
        OpsiSessionConnector connector)
    {
        _client = client;
        _session = session;
        _connector = connector;
    }

    public async Task<Result<OpsiComputerInventory>> LoadAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        Result<OpsiSession?> ensured = await _connector.EnsureConnectedAsync(cancellationToken);
        if (ensured.IsFailure)
        {
            return Result.Failure<OpsiComputerInventory>(ensured.Error!);
        }
        if (_session.Current is not { } session)
        {
            return Result.Failure<OpsiComputerInventory>(new Error(
                ErrorCode.InvalidRequest,
                "No active opsi session. Connect the opsi account under Settings."));
        }

        Task<Result<IReadOnlyList<OpsiClientHost>>> clientsTask =
            _client.GetClientsAsync(session.Connection, cancellationToken);
        Task<Result<IReadOnlyList<OpsiProductOnClient>>> agentsTask =
            _client.GetProductStatesAsync(session.Connection, ClientAgentProductId, cancellationToken);
        await Task.WhenAll(clientsTask, agentsTask).ConfigureAwait(false);

        Result<IReadOnlyList<OpsiClientHost>> clients = await clientsTask.ConfigureAwait(false);
        if (clients.IsFailure)
        {
            return Result.Failure<OpsiComputerInventory>(clients.Error!);
        }

        Result<IReadOnlyList<OpsiProductOnClient>> agents = await agentsTask.ConfigureAwait(false);
        if (agents.IsFailure)
        {
            return Result.Failure<OpsiComputerInventory>(agents.Error!);
        }

        Dictionary<string, OpsiProductOnClient> agentByClient = agents.Value
            .Where(state => !string.IsNullOrWhiteSpace(state.ClientId))
            .GroupBy(state => state.ClientId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(state => state.ModificationTime).First(),
                StringComparer.OrdinalIgnoreCase);

        int take = Math.Max(1, limit);
        IReadOnlyList<OpsiComputerInventoryItem> computers = clients.Value
            .OrderBy(client => client.Id, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .Select(client => new OpsiComputerInventoryItem(
                client.Id,
                client.Description,
                client.DepotId,
                client.LastSeen,
                agentByClient.TryGetValue(client.Id, out OpsiProductOnClient? agent)
                    ? agent.InstalledProductVersion
                    : null))
            .ToList();

        return Result.Success(new OpsiComputerInventory(
            computers,
            Truncated: clients.Value.Count > take,
            SourceScope: session.Connection.ServiceUrl.Authority));
    }
}
