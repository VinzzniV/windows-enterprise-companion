using Wec.Core.Abstractions;
using Wec.Core.Opsi;
using Wec.Core.Results;
using Wec.Modules.PatchManagement.Domain;
using Wec.Modules.PatchManagement.Persistence;

namespace Wec.Modules.PatchManagement.Application;

public sealed class PatchDashboardService
{
    private readonly IOpsiClient _opsiClient;
    private readonly OpsiSessionState _sessionState;
    private readonly OpsiSessionConnector _sessionConnector;
    private readonly IWingetManagedPackageRepository _wingetRepository;
    private readonly IClock _clock;

    public PatchDashboardService(
        IOpsiClient opsiClient,
        OpsiSessionState sessionState,
        OpsiSessionConnector sessionConnector,
        IWingetManagedPackageRepository wingetRepository,
        IClock clock)
    {
        _opsiClient = opsiClient;
        _sessionState = sessionState;
        _sessionConnector = sessionConnector;
        _wingetRepository = wingetRepository;
        _clock = clock;
    }

    internal static Error NotConnected { get; } = new(
        ErrorCode.InvalidRequest,
        "Not connected to an opsi server. Save the opsi server and account under Settings.");

    public async Task<Result<PatchDashboardResult>> GetDashboardAsync(
        string? depotFilter,
        CancellationToken cancellationToken)
    {
        Result<OpsiSession?> ensured = await _sessionConnector.EnsureConnectedAsync(cancellationToken);
        if (ensured.IsFailure)
        {
            return Result.Failure<PatchDashboardResult>(ensured.Error!);
        }
        if (_sessionState.Current is not { } session)
        {
            return Result.Failure<PatchDashboardResult>(NotConnected);
        }

        Result<IReadOnlyList<OpsiDepot>> depots = await _opsiClient.GetDepotsAsync(session.Connection, cancellationToken);
        Result<IReadOnlyList<OpsiClientHost>> clients = await _opsiClient.GetClientsAsync(session.Connection, cancellationToken);
        Result<IReadOnlyList<OpsiProduct>> products = await _opsiClient.GetProductsAsync(session.Connection, cancellationToken);
        Result<IReadOnlyList<OpsiProductOnDepot>> productsOnDepots = await _opsiClient.GetProductsOnDepotsAsync(session.Connection, cancellationToken);
        Result<IReadOnlyList<OpsiProductOnClient>> productStates = await _opsiClient.GetProductStatesAsync(session.Connection, cancellationToken);
        Error? readError = new[] { depots.Error, clients.Error, products.Error, productsOnDepots.Error, productStates.Error }
            .FirstOrDefault(error => error is not null);
        if (readError is not null)
        {
            return Result.Failure<PatchDashboardResult>(readError);
        }

        IReadOnlyList<WingetManagedPackage> wingetPackages = await _wingetRepository.ListAsync(cancellationToken);
        return Result.Success(Compose(
            session.Connection.ServiceUrl.ToString(),
            depotFilter,
            depots.Value,
            clients.Value,
            products.Value,
            productsOnDepots.Value,
            productStates.Value,
            wingetPackages,
            _clock.UtcNow));
    }

    internal static PatchDashboardResult Compose(
        string serverUrl,
        string? depotFilter,
        IReadOnlyList<OpsiDepot> depots,
        IReadOnlyList<OpsiClientHost> clients,
        IReadOnlyList<OpsiProduct> products,
        IReadOnlyList<OpsiProductOnDepot> productsOnDepots,
        IReadOnlyList<OpsiProductOnClient> productStates,
        IReadOnlyList<WingetManagedPackage> wingetPackages,
        DateTimeOffset nowUtc)
    {
        string? defaultDepotId = depots.FirstOrDefault(depot => depot.IsConfigServer)?.Id;
        var depotByClient = clients.ToDictionary(
            client => client.Id,
            client => client.DepotId ?? defaultDepotId,
            StringComparer.OrdinalIgnoreCase);
        bool hasFilter = !string.IsNullOrWhiteSpace(depotFilter);
        List<OpsiClientHost> filteredClients = hasFilter
            ? [.. clients.Where(client => string.Equals(depotByClient[client.Id], depotFilter, StringComparison.OrdinalIgnoreCase))]
            : [.. clients];
        var filteredClientIds = new HashSet<string>(filteredClients.Select(client => client.Id), StringComparer.OrdinalIgnoreCase);
        string[] relevantDepotIds = hasFilter ? [depotFilter!] : [.. depots.Select(depot => depot.Id)];

        var versionsByProduct = productsOnDepots
            .Where(entry => !hasFilter || string.Equals(entry.DepotId, depotFilter, StringComparison.OrdinalIgnoreCase))
            .GroupBy(entry => entry.ProductId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(entry => new PatchDepotVersion(
                    entry.DepotId,
                    FormatVersion(entry.ProductVersion, entry.PackageVersion)))
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
        var wingetByProduct = wingetPackages.ToDictionary(package => package.OpsiProductId, StringComparer.OrdinalIgnoreCase);

        var rows = new List<PatchProductRow>();
        foreach (IGrouping<string, OpsiProduct> productGroup in products
            .GroupBy(product => product.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            OpsiProduct product = productGroup.First();
            List<PatchDepotVersion> depotVersions = versionsByProduct.GetValueOrDefault(product.Id) ?? [];
            List<string> distinctVersions = [.. depotVersions.Select(version => version.Version).Distinct()];
            string? availableVersion = distinctVersions.Count == 1 ? distinctVersions[0] : null;
            string? referenceVersion = distinctVersions.OrderByDescending(version => version, NaturalVersionComparer.Instance).FirstOrDefault();
            string[] missingDepotIds = [.. relevantDepotIds
                .Where(depotId => !depotVersions.Any(version => string.Equals(version.DepotId, depotId, StringComparison.OrdinalIgnoreCase)))
                .Order(StringComparer.OrdinalIgnoreCase)];
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
            clientStates.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.ClientId, right.ClientId));

            int installedCount = clientStates.Count(state => state.InstallationStatus == "installed");
            int outdatedCount = clientStates.Count(state => state.State == PatchWorkflowState.UpdateAvailable);
            int failedCount = clientStates.Count(state => state.State == PatchWorkflowState.Failed);
            int pendingCount = clientStates.Count(state => state.State == PatchWorkflowState.ActionPending);
            WingetManagedPackage? winget = wingetByProduct.GetValueOrDefault(product.Id);
            string? managedDepotVersion = winget is null
                ? null
                : productsOnDepots.FirstOrDefault(item =>
                    string.Equals(item.ProductId, product.Id, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(item.DepotId, winget.DepotId, StringComparison.OrdinalIgnoreCase))?.ProductVersion;
            bool wingetUpdate = winget?.CheckStatus == "SUCCESS"
                && winget.LatestWingetVersion is not null
                && managedDepotVersion is not null
                && NaturalVersionComparer.Instance.Compare(managedDepotVersion, winget.LatestWingetVersion) < 0;
            bool checkFailed = winget?.CheckStatus is "FAILED" or "INELIGIBLE";
            PatchPackageStatus status = DerivePackageStatus(
                missingDepotIds.Length,
                distinctVersions.Count,
                outdatedCount,
                failedCount,
                pendingCount,
                wingetUpdate,
                checkFailed);
            rows.Add(new PatchProductRow(
                product.Id,
                product.Name,
                availableVersion,
                referenceVersion,
                depotVersions,
                missingDepotIds,
                status,
                DeriveProductState(clientStates),
                installedCount,
                outdatedCount,
                failedCount,
                pendingCount,
                failedCount > 0 ? $"Last action failed on {failedCount} client(s) — see the client list." : winget?.LastError,
                winget is not null,
                winget?.WingetId,
                winget?.LatestWingetVersion,
                winget?.CheckStatus ?? "MANUAL",
                winget?.CheckedAtUtc,
                winget?.LastError,
                wingetUpdate,
                clientStates));
        }

        List<PatchDepotSummary> depotSummaries = [.. depots.Select(depot => new PatchDepotSummary(
            depot.Id,
            depot.Description,
            depot.IsConfigServer,
            clients.Count(client => string.Equals(depotByClient[client.Id], depot.Id, StringComparison.OrdinalIgnoreCase))))
            .OrderBy(depot => depot.Id, StringComparer.OrdinalIgnoreCase)];
        return new PatchDashboardResult(
            serverUrl,
            hasFilter ? depotFilter : null,
            nowUtc,
            new PatchDashboardSummary(
                rows.Count,
                rows.Count(row => row.OutdatedClientCount > 0 || row.WingetUpdateAvailable),
                rows.Count(row => row.DepotVersions.Select(version => version.Version).Distinct().Count() > 1),
                rows.Count(row => row.MissingDepotIds.Count > 0),
                rows.Count(row => row.PackageStatus == PatchPackageStatus.CheckFailed),
                rows.Sum(row => row.OutdatedClientCount),
                filteredClients.Count,
                depots.Count,
                rows.Count(row => row.WingetManaged),
                rows.Count(row => row.WingetUpdateAvailable)),
            depotSummaries,
            rows);
    }

    internal static PatchWorkflowState DeriveClientState(OpsiProductOnClient state, string? installedVersion, string? targetVersion)
    {
        if (state.ActionResult == "failed")
        {
            return PatchWorkflowState.Failed;
        }
        if (state.ActionRequest is not (null or "none"))
        {
            return PatchWorkflowState.ActionPending;
        }
        if (state.InstallationStatus == "installed")
        {
            return targetVersion is not null && installedVersion is not null && installedVersion != targetVersion
                ? PatchWorkflowState.UpdateAvailable
                : PatchWorkflowState.Completed;
        }
        return PatchWorkflowState.Detected;
    }

    private static PatchWorkflowState DeriveProductState(IReadOnlyList<PatchClientState> states)
    {
        if (states.Any(state => state.State == PatchWorkflowState.Failed))
        {
            return PatchWorkflowState.Failed;
        }
        if (states.Any(state => state.State == PatchWorkflowState.ActionPending))
        {
            return PatchWorkflowState.ActionPending;
        }
        if (states.Any(state => state.State == PatchWorkflowState.UpdateAvailable))
        {
            return PatchWorkflowState.UpdateAvailable;
        }
        return states.Any(state => state.State == PatchWorkflowState.Completed)
            ? PatchWorkflowState.Completed
            : PatchWorkflowState.Detected;
    }

    internal static PatchPackageStatus DerivePackageStatus(
        int missingDepotCount,
        int distinctDepotVersionCount,
        int outdatedClientCount,
        int failedClientCount,
        int pendingActionCount,
        bool wingetUpdateAvailable = false,
        bool wingetCheckFailed = false)
    {
        if (failedClientCount > 0 || wingetCheckFailed)
        {
            return PatchPackageStatus.CheckFailed;
        }
        if (missingDepotCount > 0)
        {
            return PatchPackageStatus.MissingOnDepot;
        }
        if (distinctDepotVersionCount > 1)
        {
            return PatchPackageStatus.DepotDeviation;
        }
        if (pendingActionCount > 0)
        {
            return PatchPackageStatus.ActionPending;
        }
        return outdatedClientCount > 0 || wingetUpdateAvailable ? PatchPackageStatus.UpdateAvailable : PatchPackageStatus.Current;
    }

    private static string FormatVersion(string productVersion, string packageVersion) =>
        packageVersion.Length == 0 ? productVersion : $"{productVersion}-{packageVersion}";

    private sealed class NaturalVersionComparer : IComparer<string>
    {
        public static NaturalVersionComparer Instance { get; } = new();
        public int Compare(string? left, string? right)
        {
            string[] leftParts = (left ?? string.Empty).Split(['.', '-', '_']);
            string[] rightParts = (right ?? string.Empty).Split(['.', '-', '_']);
            for (int index = 0; index < Math.Max(leftParts.Length, rightParts.Length); index++)
            {
                string leftPart = index < leftParts.Length ? leftParts[index] : "0";
                string rightPart = index < rightParts.Length ? rightParts[index] : "0";
                int comparison = long.TryParse(leftPart, out long leftNumber)
                    && long.TryParse(rightPart, out long rightNumber)
                        ? leftNumber.CompareTo(rightNumber)
                        : StringComparer.OrdinalIgnoreCase.Compare(leftPart, rightPart);
                if (comparison != 0)
                {
                    return comparison;
                }
            }
            return 0;
        }
    }
}
