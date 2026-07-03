using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Opsi;
using Wec.Core.Results;
using Wec.Modules.PatchManagement.Domain;
using Wec.Modules.PatchManagement.Persistence;

namespace Wec.Modules.PatchManagement.Application;

public sealed class PatchDashboardService
{
    private readonly IOpsiClient _opsiClient;
    private readonly OpsiSessionState _sessionState;
    private readonly IInstalledSoftwareInventoryProvider _softwareInventory;
    private readonly IPatchMappingRepository _mappingRepository;
    private readonly IClock _clock;

    public PatchDashboardService(
        IOpsiClient opsiClient,
        OpsiSessionState sessionState,
        IInstalledSoftwareInventoryProvider softwareInventory,
        IPatchMappingRepository mappingRepository,
        IClock clock)
    {
        _opsiClient = opsiClient;
        _sessionState = sessionState;
        _softwareInventory = softwareInventory;
        _mappingRepository = mappingRepository;
        _clock = clock;
    }

    internal static Error NotConnected { get; } = new(
        ErrorCode.InvalidRequest,
        "Not connected to an opsi server. Test the connection first — credentials are kept for this session only.");

    public async Task<Result<PatchDashboardResult>> GetDashboardAsync(
        string? depotFilter, CancellationToken cancellationToken)
    {
        if (_sessionState.Current is not { } session)
        {
            return Result.Failure<PatchDashboardResult>(NotConnected);
        }

        OpsiConnection connection = session.Connection;
        Result<IReadOnlyList<OpsiDepot>> depots =
            await _opsiClient.GetDepotsAsync(connection, cancellationToken);
        if (depots.IsFailure)
        {
            return Result.Failure<PatchDashboardResult>(depots.Error!);
        }

        Result<IReadOnlyList<OpsiClientHost>> clients =
            await _opsiClient.GetClientsAsync(connection, cancellationToken);
        if (clients.IsFailure)
        {
            return Result.Failure<PatchDashboardResult>(clients.Error!);
        }

        Result<IReadOnlyList<OpsiProduct>> products =
            await _opsiClient.GetProductsAsync(connection, cancellationToken);
        if (products.IsFailure)
        {
            return Result.Failure<PatchDashboardResult>(products.Error!);
        }

        Result<IReadOnlyList<OpsiProductOnDepot>> productsOnDepots =
            await _opsiClient.GetProductsOnDepotsAsync(connection, cancellationToken);
        if (productsOnDepots.IsFailure)
        {
            return Result.Failure<PatchDashboardResult>(productsOnDepots.Error!);
        }

        Result<IReadOnlyList<OpsiProductOnClient>> productStates =
            await _opsiClient.GetProductStatesAsync(connection, cancellationToken);
        if (productStates.IsFailure)
        {
            return Result.Failure<PatchDashboardResult>(productStates.Error!);
        }

        IReadOnlyList<HostInstalledSoftwareData> inventoryHosts =
            await _softwareInventory.GetAllHostsAsync(cancellationToken);
        IReadOnlyList<ProductMapping> mappings = await _mappingRepository.ListAsync(cancellationToken);

        return Result.Success(Compose(
            connection.ServiceUrl.ToString(),
            depotFilter,
            depots.Value,
            clients.Value,
            products.Value,
            productsOnDepots.Value,
            productStates.Value,
            inventoryHosts,
            mappings,
            _clock.UtcNow));
    }

    /// <summary>Pure composition — everything above is fetch, everything here is logic.</summary>
    internal static PatchDashboardResult Compose(
        string serverUrl,
        string? depotFilter,
        IReadOnlyList<OpsiDepot> depots,
        IReadOnlyList<OpsiClientHost> clients,
        IReadOnlyList<OpsiProduct> products,
        IReadOnlyList<OpsiProductOnDepot> productsOnDepots,
        IReadOnlyList<OpsiProductOnClient> productStates,
        IReadOnlyList<HostInstalledSoftwareData> inventoryHosts,
        IReadOnlyList<ProductMapping> mappings,
        DateTimeOffset nowUtc)
    {
        string? defaultDepotId = depots.FirstOrDefault(depot => depot.IsConfigServer)?.Id;
        var depotByClient = clients.ToDictionary(
            client => client.Id,
            client => client.DepotId ?? defaultDepotId,
            StringComparer.OrdinalIgnoreCase);

        bool hasFilter = !string.IsNullOrWhiteSpace(depotFilter);
        List<OpsiClientHost> filteredClients = hasFilter
            ? [.. clients.Where(client => string.Equals(
                depotByClient[client.Id], depotFilter, StringComparison.OrdinalIgnoreCase))]
            : [.. clients];
        var filteredClientIds = new HashSet<string>(
            filteredClients.Select(client => client.Id), StringComparer.OrdinalIgnoreCase);

        // productId → per-depot versions; the filter narrows which depots count
        var versionsByProduct = productsOnDepots
            .Where(entry => !hasFilter || string.Equals(entry.DepotId, depotFilter, StringComparison.OrdinalIgnoreCase))
            .GroupBy(entry => entry.ProductId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(entry => new PatchDepotVersion(entry.DepotId, FormatVersion(entry.ProductVersion, entry.PackageVersion)))
                    .OrderBy(version => version.DepotId, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);
        var depotVersionLookup = productsOnDepots
            .GroupBy(entry => (entry.ProductId, entry.DepotId))
            .ToDictionary(
                group => group.Key,
                group => FormatVersion(group.First().ProductVersion, group.First().PackageVersion));

        var statesByProduct = productStates
            .Where(state => filteredClientIds.Contains(state.ClientId))
            .GroupBy(state => state.ProductId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var mappingsByProduct = mappings
            .GroupBy(mapping => mapping.OpsiProductId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(mapping => mapping.SoftwareName).ToList(),
                StringComparer.OrdinalIgnoreCase);
        var mappedSoftwareNames = new HashSet<string>(
            mappings.Select(mapping => mapping.SoftwareName), StringComparer.OrdinalIgnoreCase);

        var productRows = new List<PatchProductRow>();
        foreach (OpsiProduct product in products
            .Where(product => versionsByProduct.ContainsKey(product.Id))
            .OrderBy(product => product.Id, StringComparer.OrdinalIgnoreCase))
        {
            List<PatchDepotVersion> depotVersions = versionsByProduct[product.Id];
            List<string> distinctVersions = [.. depotVersions.Select(version => version.Version).Distinct()];
            string? availableVersion = distinctVersions.Count == 1 ? distinctVersions[0] : null;

            var clientStates = new List<PatchClientState>();
            foreach (OpsiProductOnClient state in statesByProduct.GetValueOrDefault(product.Id) ?? [])
            {
                string? clientDepot = depotByClient.GetValueOrDefault(state.ClientId);
                string? targetVersion = clientDepot is not null
                    && depotVersionLookup.TryGetValue((product.Id, clientDepot), out string? depotVersion)
                    ? depotVersion
                    : availableVersion;
                string? installedVersion = state.InstalledProductVersion is null
                    ? null
                    : FormatVersion(state.InstalledProductVersion, state.InstalledPackageVersion ?? string.Empty);
                clientStates.Add(new PatchClientState(
                    state.ClientId,
                    clientDepot,
                    installedVersion,
                    targetVersion,
                    state.InstallationStatus,
                    state.ActionRequest,
                    state.ActionResult,
                    DeriveClientState(state, installedVersion, targetVersion)));
            }

            clientStates.Sort((left, right) =>
                StringComparer.OrdinalIgnoreCase.Compare(left.ClientId, right.ClientId));

            int installedCount = clientStates.Count(state => state.InstallationStatus == "installed");
            int outdatedCount = clientStates.Count(state => state.State == PatchWorkflowState.UpdateAvailable);
            int failedCount = clientStates.Count(state => state.State == PatchWorkflowState.Failed);
            int pendingCount = clientStates.Count(state => state.State == PatchWorkflowState.RolloutRequested);

            List<string> productMappings = mappingsByProduct.GetValueOrDefault(product.Id) ?? [];
            List<InventoryDetection> detections = [.. inventoryHosts
                .SelectMany(host => host.Software
                    .Where(entry => productMappings.Contains(entry.Name, StringComparer.OrdinalIgnoreCase))
                    .Select(entry => new InventoryDetection(host.Host, entry.Version)))
                .OrderBy(detection => detection.Host, StringComparer.OrdinalIgnoreCase)];

            productRows.Add(new PatchProductRow(
                product.Id,
                product.Name,
                availableVersion,
                depotVersions,
                DeriveProductState(clientStates),
                installedCount,
                outdatedCount,
                failedCount,
                pendingCount,
                failedCount > 0
                    ? $"Last action failed on {failedCount} client(s) — see the client list."
                    : null,
                clientStates,
                productMappings,
                detections));
        }

        List<UnmappedSoftware> unmapped = [.. inventoryHosts
            .SelectMany(host => host.Software.Select(entry => (host.Host, entry)))
            .Where(item => !mappedSoftwareNames.Contains(item.entry.Name))
            .GroupBy(item => item.entry.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => new UnmappedSoftware(
                group.Key,
                [.. group.Select(item => item.entry.Version).Where(version => version is not null)
                    .Cast<string>().Distinct().Order(StringComparer.OrdinalIgnoreCase)],
                group.Select(item => item.Host).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                SuggestProductId(group.Key, products)))
            .OrderByDescending(software => software.HostCount)
            .ThenBy(software => software.Name, StringComparer.OrdinalIgnoreCase)];

        List<PatchDepotSummary> depotSummaries = [.. depots
            .Select(depot => new PatchDepotSummary(
                depot.Id,
                depot.Description,
                depot.IsConfigServer,
                clients.Count(client => string.Equals(
                    depotByClient[client.Id], depot.Id, StringComparison.OrdinalIgnoreCase))))
            .OrderBy(depot => depot.Id, StringComparer.OrdinalIgnoreCase)];

        return new PatchDashboardResult(
            serverUrl,
            hasFilter ? depotFilter : null,
            nowUtc,
            new PatchDashboardSummary(
                productRows.Count,
                productRows.Count(row => row.OutdatedClientCount > 0),
                productRows.Count(row => row.FailedClientCount > 0),
                productRows.Sum(row => row.PendingActionCount),
                filteredClients.Count,
                depots.Count,
                unmapped.Count),
            depotSummaries,
            productRows,
            unmapped);
    }

    internal static PatchWorkflowState DeriveClientState(
        OpsiProductOnClient state, string? installedVersion, string? targetVersion)
    {
        if (state.ActionResult == "failed")
        {
            return PatchWorkflowState.Failed;
        }

        if (state.ActionRequest is not (null or "none"))
        {
            return PatchWorkflowState.RolloutRequested;
        }

        if (state.InstallationStatus == "installed")
        {
            return targetVersion is not null && installedVersion is not null && installedVersion != targetVersion
                ? PatchWorkflowState.UpdateAvailable
                : PatchWorkflowState.Completed;
        }

        return PatchWorkflowState.Detected;
    }

    private static PatchWorkflowState DeriveProductState(IReadOnlyList<PatchClientState> clientStates)
    {
        if (clientStates.Any(state => state.State == PatchWorkflowState.Failed))
        {
            return PatchWorkflowState.Failed;
        }

        if (clientStates.Any(state => state.State == PatchWorkflowState.RolloutRequested))
        {
            return PatchWorkflowState.RolloutRequested;
        }

        if (clientStates.Any(state => state.State == PatchWorkflowState.UpdateAvailable))
        {
            return PatchWorkflowState.UpdateAvailable;
        }

        return clientStates.Any(state => state.State == PatchWorkflowState.Completed)
            ? PatchWorkflowState.Completed
            : PatchWorkflowState.Detected;
    }

    // Suggestions only on exact name matches — a wrong automatic match on
    // a patch tool is worse than a manual step (ADR 0008)
    private static string? SuggestProductId(string softwareName, IReadOnlyList<OpsiProduct> products) =>
        products.FirstOrDefault(product =>
            string.Equals(product.Id, softwareName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(product.Name, softwareName, StringComparison.OrdinalIgnoreCase))?.Id;

    private static string FormatVersion(string productVersion, string packageVersion) =>
        packageVersion.Length == 0 ? productVersion : $"{productVersion}-{packageVersion}";
}
