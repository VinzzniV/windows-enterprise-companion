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

public sealed record PatchDashboardSummary(
    int ProductCount,
    int ProductsWithUpdates,
    int ProductsWithFailures,
    int PendingRolloutCount,
    int ClientCount,
    int DepotCount,
    int UnmappedSoftwareCount);

public sealed record PatchDepotSummary(string Id, string? Description, bool IsConfigServer, int ClientCount);

public sealed record PatchProductRow(
    string ProductId,
    string? Name,
    string? AvailableVersion,
    IReadOnlyList<PatchDepotVersion> DepotVersions,
    PatchWorkflowState State,
    int InstalledClientCount,
    int OutdatedClientCount,
    int FailedClientCount,
    int PendingActionCount,
    string? LastError,
    IReadOnlyList<PatchClientState> Clients,
    IReadOnlyList<string> MappedSoftwareNames,
    IReadOnlyList<InventoryDetection> InventoryDetections);

public sealed record PatchDepotVersion(string DepotId, string Version);

public sealed record PatchClientState(
    string ClientId,
    string? DepotId,
    string? InstalledVersion,
    string? TargetVersion,
    string? InstallationStatus,
    string? ActionRequest,
    string? ActionResult,
    PatchWorkflowState State);

public sealed record InventoryDetection(string Host, string? Version);

public sealed record UnmappedSoftware(
    string Name,
    IReadOnlyList<string> Versions,
    int HostCount,
    string? SuggestedProductId);
