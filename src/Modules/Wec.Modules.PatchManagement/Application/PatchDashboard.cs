using Wec.Modules.PatchManagement.Domain;

namespace Wec.Modules.PatchManagement.Application;

public sealed record PatchDashboardResult(
    string ServerUrl,
    string? DepotFilter,
    DateTimeOffset GeneratedAtUtc,
    PatchDashboardSummary Summary,
    IReadOnlyList<PatchDepotSummary> Depots,
    IReadOnlyList<PatchProductRow> Products,
    IReadOnlyList<UnmappedSoftware> UnmappedSoftware);

public sealed record PatchDashboardOverview(
    string ServerUrl,
    string? DepotFilter,
    DateTimeOffset GeneratedAtUtc,
    PatchDashboardSummary Summary,
    IReadOnlyList<PatchDepotSummary> Depots,
    IReadOnlyList<PatchProductOverviewRow> Products,
    IReadOnlyList<UnmappedSoftware> UnmappedSoftware);

public sealed record PatchDashboardSummary(
    int ProductCount,
    int ProductsWithUpdates,
    int ProductsWithDepotDeviation,
    int ProductsMissingOnDepots,
    int ProductsWithFailures,
    int PendingRolloutCount,
    int OutdatedClientCount,
    int ClientCount,
    int DepotCount,
    int UnmappedSoftwareCount);

public sealed record PatchDepotSummary(string Id, string? Description, bool IsConfigServer, int ClientCount);

public sealed record PatchProductRow(
    string ProductId,
    string? Name,
    string? AvailableVersion,
    string? ReferenceVersion,
    string? ManufacturerVersion,
    string ManufacturerCheckStatus,
    DateTimeOffset? ManufacturerCheckedAtUtc,
    string? ManufacturerCheckError,
    bool ManufacturerUpdateAvailable,
    IReadOnlyList<PatchDepotVersion> DepotVersions,
    IReadOnlyList<string> MissingDepotIds,
    PatchPackageStatus PackageStatus,
    PatchWorkflowState State,
    int InstalledClientCount,
    int OutdatedClientCount,
    int FailedClientCount,
    int PendingActionCount,
    string? LastError,
    IReadOnlyList<PatchClientState> Clients,
    IReadOnlyList<string> MappedSoftwareNames,
    IReadOnlyList<InventoryDetection> InventoryDetections);

public sealed record PatchProductOverviewRow(
    string ProductId,
    string? Name,
    string? AvailableVersion,
    string? ReferenceVersion,
    string? ManufacturerVersion,
    string ManufacturerCheckStatus,
    DateTimeOffset? ManufacturerCheckedAtUtc,
    string? ManufacturerCheckError,
    bool ManufacturerUpdateAvailable,
    IReadOnlyList<PatchDepotVersion> DepotVersions,
    IReadOnlyList<string> MissingDepotIds,
    PatchPackageStatus PackageStatus,
    PatchWorkflowState State,
    int InstalledClientCount,
    int OutdatedClientCount,
    int FailedClientCount,
    int PendingActionCount,
    string? LastError,
    IReadOnlyList<string> MappedSoftwareNames,
    IReadOnlyList<InventoryDetection> InventoryDetections);

public sealed record PatchDepotVersion(string DepotId, string Version);

public enum PatchPackageStatus
{
    Current = 0,
    UpdateAvailable,
    DepotDeviation,
    MissingOnDepot,
    CheckFailed,
    DeploymentPending,
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

public sealed record PatchClientListItem(
    string ProductId,
    string? ProductName,
    PatchClientState Client);

public sealed record PatchClientStatePage(
    IReadOnlyList<PatchClientListItem> Items,
    int Total,
    int SnapshotTotal,
    int Page,
    int PageSize);

public sealed record InventoryDetection(string Host, string? Version);

public sealed record UnmappedSoftware(
    string Name,
    IReadOnlyList<string> Versions,
    int HostCount,
    string? SuggestedProductId);
