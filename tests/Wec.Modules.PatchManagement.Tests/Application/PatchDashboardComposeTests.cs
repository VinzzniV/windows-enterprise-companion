using Wec.Core.Contracts;
using Wec.Core.Opsi;
using Wec.Modules.PatchManagement.Application;
using Wec.Modules.PatchManagement.Domain;
using Wec.Modules.PatchManagement.Persistence;

namespace Wec.Modules.PatchManagement.Tests.Application;

public sealed class PatchDashboardComposeTests
{
    private const string ConfigServer = "opsi.kauth.local";
    private const string DenkingenDepot = "depot-denkingen.kauth.local";

    private static readonly DateTimeOffset Now = new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyList<OpsiDepot> Depots =
    [
        new(ConfigServer, "Main server", IsConfigServer: true),
        new(DenkingenDepot, "Denkingen", IsConfigServer: false),
    ];

    private static readonly IReadOnlyList<OpsiClientHost> Clients =
    [
        new("pc1.kauth.local", null, DenkingenDepot, null),
        new("pc2.kauth.local", null, DenkingenDepot, null),
        new("pc3.kauth.local", null, DepotId: null, null), // default depot = configserver
    ];

    private static readonly IReadOnlyList<OpsiProduct> Products =
    [
        new("firefox", "Mozilla Firefox", "128.0", "2", null),
        new("7zip", "7-Zip", "24.08", "1", null),
    ];

    private static readonly IReadOnlyList<OpsiProductOnDepot> ProductsOnDepots =
    [
        new("firefox", DenkingenDepot, "128.0", "2"),
        new("firefox", ConfigServer, "127.0", "1"),
        new("7zip", DenkingenDepot, "24.08", "1"),
    ];

    private static readonly IReadOnlyList<OpsiProductOnClient> States =
    [
        new("firefox", "pc1.kauth.local", "installed", "none", "successful", "127.0", "1", null),
        new("firefox", "pc2.kauth.local", "installed", "none", "successful", "128.0", "2", null),
        new("7zip", "pc2.kauth.local", "unknown", "none", "failed", null, null, null),
        new("firefox", "pc3.kauth.local", "installed", "none", "successful", "127.0", "1", null),
    ];

    private static readonly IReadOnlyList<HostInstalledSoftwareData> InventoryHosts =
    [
        new("HOST-A", Now, [
            new InstalledSoftwareRecordData("Mozilla Firefox", "127.0", "Mozilla"),
            new InstalledSoftwareRecordData("Notepad++", "8.6", null),
        ]),
    ];

    private static readonly IReadOnlyList<ProductMapping> Mappings =
        [new("Mozilla Firefox", "firefox")];

    private static PatchDashboardResult ComposeWith(string? depotFilter) =>
        PatchDashboardService.Compose(
            "https://opsi.kauth.local:4447/", depotFilter,
            Depots, Clients, Products, ProductsOnDepots, States, InventoryHosts, Mappings, Now);

    [Fact]
    public void DepotFilter_LimitsClientsProductsAndVersions()
    {
        PatchDashboardResult dashboard = ComposeWith(DenkingenDepot);

        Assert.Equal(2, dashboard.Summary.ClientCount);
        Assert.Equal(2, dashboard.Summary.ProductCount);

        PatchProductRow firefox = Assert.Single(dashboard.Products, row => row.ProductId == "firefox");
        Assert.Equal("128.0-2", firefox.AvailableVersion);
        Assert.Equal(PatchWorkflowState.UpdateAvailable, firefox.State);
        Assert.Equal(1, firefox.OutdatedClientCount);
        Assert.DoesNotContain(firefox.Clients, client => client.ClientId == "pc3.kauth.local");

        PatchClientState outdated = Assert.Single(firefox.Clients, client => client.ClientId == "pc1.kauth.local");
        Assert.Equal("127.0-1", outdated.InstalledVersion);
        Assert.Equal("128.0-2", outdated.TargetVersion);
        Assert.Equal(PatchWorkflowState.UpdateAvailable, outdated.State);
    }

    [Fact]
    public void FailedAction_MarksProductFailedWithLastError()
    {
        PatchDashboardResult dashboard = ComposeWith(DenkingenDepot);

        PatchProductRow sevenZip = Assert.Single(dashboard.Products, row => row.ProductId == "7zip");
        Assert.Equal(PatchWorkflowState.Failed, sevenZip.State);
        Assert.Equal(1, sevenZip.FailedClientCount);
        Assert.NotNull(sevenZip.LastError);
        Assert.Equal(1, dashboard.Summary.ProductsWithFailures);
    }

    [Fact]
    public void WithoutFilter_ClientOnDefaultDepotIsUpToDateAgainstItsOwnDepotVersion()
    {
        PatchDashboardResult dashboard = ComposeWith(null);

        Assert.Equal(3, dashboard.Summary.ClientCount);
        PatchProductRow firefox = Assert.Single(dashboard.Products, row => row.ProductId == "firefox");
        // Two depots carry different firefox versions — no single available version
        Assert.Null(firefox.AvailableVersion);
        Assert.Equal("128.0-2", firefox.ReferenceVersion);
        Assert.Equal(2, firefox.DepotVersions.Count);
        Assert.Equal(PatchPackageStatus.DepotDeviation, firefox.PackageStatus);

        PatchClientState pc3 = Assert.Single(firefox.Clients, client => client.ClientId == "pc3.kauth.local");
        Assert.Equal(ConfigServer, pc3.DepotId);
        Assert.Equal("127.0-1", pc3.TargetVersion);
        Assert.Equal(PatchWorkflowState.Completed, pc3.State);
    }

    [Fact]
    public void MappedInventorySoftware_AppearsAsDetectionNotAsUnmapped()
    {
        PatchDashboardResult dashboard = ComposeWith(null);

        PatchProductRow firefox = Assert.Single(dashboard.Products, row => row.ProductId == "firefox");
        InventoryDetection detection = Assert.Single(firefox.InventoryDetections);
        Assert.Equal("HOST-A", detection.Host);
        Assert.Contains("Mozilla Firefox", firefox.MappedSoftwareNames);

        UnmappedSoftware unmapped = Assert.Single(dashboard.UnmappedSoftware);
        Assert.Equal("Notepad++", unmapped.Name);
        Assert.Equal(1, unmapped.HostCount);
        Assert.Null(unmapped.SuggestedProductId);
    }

    [Fact]
    public void ExactProductNameMatch_IsSuggestedForUnmappedSoftware()
    {
        IReadOnlyList<HostInstalledSoftwareData> inventory =
            [new("HOST-B", Now, [new InstalledSoftwareRecordData("7-Zip", "23.01", null)])];

        PatchDashboardResult dashboard = PatchDashboardService.Compose(
            "https://opsi.kauth.local:4447/", null,
            Depots, Clients, Products, ProductsOnDepots, States, inventory, [], Now);

        UnmappedSoftware sevenZip = Assert.Single(
            dashboard.UnmappedSoftware, software => software.Name == "7-Zip");
        Assert.Equal("7zip", sevenZip.SuggestedProductId);
    }

    [Fact]
    public void NewerManufacturerVersion_MarksPackageAsUpdateAvailable()
    {
        PatchDashboardResult dashboard = PatchDashboardService.Compose(
            "https://opsi.kauth.local:4447/",
            DenkingenDepot,
            Depots,
            Clients,
            Products,
            ProductsOnDepots,
            States.Where(state => state.ProductId == "firefox" && state.ClientId == "pc2.kauth.local").ToList(),
            InventoryHosts,
            Mappings,
            Now,
            [new ProductVersionSource(
                "firefox",
                "https://vendor.example/releases",
                "Version ([0-9.]+)",
                true,
                "129.0",
                Now,
                "SUCCESS",
                null)]);

        PatchProductRow firefox = Assert.Single(dashboard.Products, row => row.ProductId == "firefox");
        Assert.True(firefox.ManufacturerUpdateAvailable);
        Assert.Equal("129.0", firefox.ManufacturerVersion);
        Assert.Equal(PatchPackageStatus.UpdateAvailable, firefox.PackageStatus);
        Assert.Equal(1, dashboard.Summary.ProductsWithUpdates);
    }

    [Fact]
    public void LatestFailedPackageOperation_IsCentralUntilTheSameDepotSucceeds()
    {
        var failed = new PatchAuditEntry(
            1,
            Now.AddMinutes(-2),
            "admin",
            PatchActionService.TestPackageUpdateAction,
            "firefox",
            DenkingenDepot,
            [],
            null,
            "FAILED",
            "Repository download failed.",
            "128.0-2",
            null);
        PatchDashboardResult failedDashboard = PatchDashboardService.Compose(
            "https://opsi.kauth.local:4447/",
            DenkingenDepot,
            Depots,
            Clients,
            [Products[0]],
            ProductsOnDepots,
            [States[1]],
            InventoryHosts,
            Mappings,
            Now,
            versionSources: null,
            auditEntries: [failed]);

        PatchProductRow failedFirefox = Assert.Single(failedDashboard.Products);
        Assert.Equal(PatchPackageStatus.CheckFailed, failedFirefox.PackageStatus);
        Assert.Equal("Repository download failed.", failedFirefox.LastError);
        Assert.Equal(1, failedDashboard.Summary.ProductsWithFailures);

        PatchDashboardResult recoveredDashboard = PatchDashboardService.Compose(
            "https://opsi.kauth.local:4447/",
            DenkingenDepot,
            Depots,
            Clients,
            [Products[0]],
            ProductsOnDepots,
            [States[1]],
            InventoryHosts,
            Mappings,
            Now,
            versionSources: null,
            auditEntries:
            [
                failed,
                failed with
                {
                    Id = 2,
                    TimestampUtc = Now.AddMinutes(-1),
                    Result = "SUCCESS",
                    ErrorMessage = null,
                    NewVersion = "128.0-2",
                },
            ]);

        PatchProductRow recoveredFirefox = Assert.Single(recoveredDashboard.Products);
        Assert.Equal(PatchPackageStatus.Current, recoveredFirefox.PackageStatus);
        Assert.Null(recoveredFirefox.LastError);
        Assert.Equal(0, recoveredDashboard.Summary.ProductsWithFailures);
    }

    [Fact]
    public void DepotSummaries_CountClientsIncludingDefaultDepotFallback()
    {
        PatchDashboardResult dashboard = ComposeWith(null);

        Assert.Equal(2, Assert.Single(dashboard.Depots, depot => depot.Id == DenkingenDepot).ClientCount);
        Assert.Equal(1, Assert.Single(dashboard.Depots, depot => depot.Id == ConfigServer).ClientCount);
    }

    [Fact]
    public void MissingPackageOnDepot_IsReportedCentrally()
    {
        PatchDashboardResult dashboard = ComposeWith(null);

        PatchProductRow sevenZip = Assert.Single(dashboard.Products, row => row.ProductId == "7zip");
        Assert.Equal(PatchPackageStatus.CheckFailed, sevenZip.PackageStatus);
        Assert.Equal([ConfigServer], sevenZip.MissingDepotIds);
        Assert.Equal(1, dashboard.Summary.ProductsMissingOnDepots);
        Assert.Equal(1, dashboard.Summary.ProductsWithDepotDeviation);
    }

    [Theory]
    [InlineData(0, 1, 0, 0, 0, PatchPackageStatus.Current)]
    [InlineData(0, 1, 1, 0, 0, PatchPackageStatus.UpdateAvailable)]
    [InlineData(0, 1, 0, 0, 1, PatchPackageStatus.DeploymentPending)]
    [InlineData(0, 2, 0, 0, 0, PatchPackageStatus.DepotDeviation)]
    [InlineData(1, 1, 0, 0, 0, PatchPackageStatus.MissingOnDepot)]
    [InlineData(1, 2, 1, 1, 1, PatchPackageStatus.CheckFailed)]
    public void PackageStatus_UsesOperationalPriority(
        int missing,
        int versions,
        int outdated,
        int failed,
        int pending,
        PatchPackageStatus expected)
    {
        Assert.Equal(expected, PatchDashboardService.DerivePackageStatus(
            missing, versions, outdated, failed, pending));
    }

    [Theory]
    [InlineData("failed", "none", "installed", PatchWorkflowState.Failed)]
    [InlineData("successful", "setup", "installed", PatchWorkflowState.RolloutRequested)]
    [InlineData("successful", "none", "installed", PatchWorkflowState.Completed)]
    [InlineData(null, "none", "not_installed", PatchWorkflowState.Detected)]
    [InlineData(null, null, null, PatchWorkflowState.Detected)]
    public void DeriveClientState_CoversTheMvpStates(
        string? actionResult, string? actionRequest, string? installationStatus, PatchWorkflowState expected)
    {
        var state = new OpsiProductOnClient(
            "firefox", "pc1", installationStatus, actionRequest, actionResult, "1.0", "1", null);

        Assert.Equal(
            expected,
            PatchDashboardService.DeriveClientState(state, "1.0-1", "1.0-1"));
    }

    [Fact]
    public void DeriveClientState_InstalledWithOlderVersion_IsUpdateAvailable()
    {
        var state = new OpsiProductOnClient(
            "firefox", "pc1", "installed", "none", "successful", "1.0", "1", null);

        Assert.Equal(
            PatchWorkflowState.UpdateAvailable,
            PatchDashboardService.DeriveClientState(state, "1.0-1", "2.0-1"));
    }
}
