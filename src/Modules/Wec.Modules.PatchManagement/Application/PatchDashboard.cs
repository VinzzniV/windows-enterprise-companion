using Wec.Modules.PatchManagement.Domain;

namespace Wec.Modules.PatchManagement.Application;

public sealed record PatchDashboardResult(
    string ServerUrl,
    string? DepotFilter,
    DateTimeOffset GeneratedAtUtc,
    PatchDashboardSummary Summary,
    IReadOnlyList<PatchDepotSummary> Depots,
    IReadOnlyList<PatchProductRow> Products);

public sealed record PatchDashboardOverview(
    string ServerUrl,
    string? DepotFilter,
    DateTimeOffset GeneratedAtUtc,
    PatchDashboardSummary Summary,
    IReadOnlyList<PatchDepotSummary> Depots,
    IReadOnlyList<PatchProductOverviewRow> Products);

public sealed record PatchDashboardSummary(
    int ProductCount,
    int ProductsWithUpdates,
    int ProductsWithDepotDeviation,
    int ProductsMissingOnDepots,
    int ProductsWithFailures,
    int OutdatedClientCount,
    int ClientCount,
    int DepotCount,
    int WingetManagedCount,
    int WingetUpdatesAvailable);

public sealed record PatchDepotSummary(string Id, string? Description, bool IsConfigServer, int ClientCount);

public sealed record PatchProductRow(
    string ProductId,
    string? Name,
    string? AvailableVersion,
    string? ReferenceVersion,
    IReadOnlyList<PatchDepotVersion> DepotVersions,
    IReadOnlyList<string> MissingDepotIds,
    PatchPackageStatus PackageStatus,
    PatchWorkflowState State,
    int InstalledClientCount,
    int OutdatedClientCount,
    int FailedClientCount,
    int PendingActionCount,
    string? LastError,
    bool WingetManaged,
    string? WingetId,
    string? LatestWingetVersion,
    string WingetCheckStatus,
    DateTimeOffset? WingetCheckedAtUtc,
    string? WingetCheckError,
    bool WingetUpdateAvailable,
    IReadOnlyList<PatchClientState> Clients);

public sealed record PatchProductOverviewRow(
    string ProductId,
    string? Name,
    string? AvailableVersion,
    string? ReferenceVersion,
    IReadOnlyList<PatchDepotVersion> DepotVersions,
    IReadOnlyList<string> MissingDepotIds,
    PatchPackageStatus PackageStatus,
    PatchWorkflowState State,
    int InstalledClientCount,
    int OutdatedClientCount,
    int FailedClientCount,
    int PendingActionCount,
    string? LastError,
    bool WingetManaged,
    string? WingetId,
    string? LatestWingetVersion,
    string WingetCheckStatus,
    DateTimeOffset? WingetCheckedAtUtc,
    string? WingetCheckError,
    bool WingetUpdateAvailable);

public sealed record PatchDepotVersion(string DepotId, string Version);

public enum PatchPackageStatus
{
    Current = 0,
    UpdateAvailable,
    DepotDeviation,
    MissingOnDepot,
    CheckFailed,
    ActionPending,
}

public sealed record PatchClientState(
    string ClientId,
    string? DepotId,
    string? InstalledVersion,
    string? TargetVersion,
    string? InstallationStatus,
    string? ActionRequest,
    string? ActionResult,
    PatchWorkflowState State);

public sealed record PatchClientListItem(string ProductId, string? ProductName, PatchClientState Client);

public sealed record PatchClientStatePage(
    IReadOnlyList<PatchClientListItem> Items,
    int Total,
    int SnapshotTotal,
    int Page,
    int PageSize);
