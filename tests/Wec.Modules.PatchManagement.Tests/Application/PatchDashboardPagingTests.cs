using Wec.Modules.PatchManagement.Application;
using Wec.Modules.PatchManagement.Domain;

namespace Wec.Modules.PatchManagement.Tests.Application;

public sealed class PatchDashboardPagingTests
{
    [Fact]
    public void SnapshotCache_IsBoundToServerUserAndDepot_AndReplacesAtomically()
    {
        var cache = new PatchDashboardSnapshotCache();
        var identity = new PatchDashboardSnapshotIdentity(
            "https://opsi.example:4447/", "admin", "depot-a");
        PatchDashboardResult first = Dashboard([Client("pc001", PatchWorkflowState.Completed)]);
        PatchDashboardResult second = Dashboard([Client("pc002", PatchWorkflowState.Failed)]);

        Assert.False(cache.TryGet(identity, out _));
        cache.Store(identity, first);

        Assert.True(cache.TryGet(identity, out PatchDashboardResult? cachedFirst));
        Assert.Same(first, cachedFirst);
        Assert.False(cache.TryGet(identity with { DepotFilter = "depot-b" }, out _));
        Assert.False(cache.TryGet(identity with { UserName = "other" }, out _));

        cache.Store(identity, second);

        Assert.True(cache.TryGet(identity, out PatchDashboardResult? cachedSecond));
        Assert.Same(second, cachedSecond);
    }

    [Fact]
    public void Overview_RemovesClientArraysButPreservesProductMetadata()
    {
        PatchDashboardResult dashboard = Dashboard([
            Client("pc001", PatchWorkflowState.UpdateAvailable),
            Client("pc002", PatchWorkflowState.Failed),
        ]);

        PatchDashboardOverview overview = PatchDashboardPaging.Overview(dashboard);

        PatchProductOverviewRow product = Assert.Single(overview.Products);
        Assert.Equal("alpha", product.ProductId);
        Assert.Equal(2, product.InstalledClientCount);
        Assert.Equal(dashboard.GeneratedAtUtc, overview.GeneratedAtUtc);
    }

    [Fact]
    public void Page_AppliesServerFiltersSortAndBoundsPageSize()
    {
        List<PatchClientState> clients = Enumerable.Range(1, 105)
            .Select(index => Client(
                $"pc{index:000}",
                index % 2 == 0 ? PatchWorkflowState.Failed : PatchWorkflowState.Completed))
            .ToList();
        PatchDashboardResult dashboard = Dashboard(clients);

        PatchClientStatePage filtered = PatchDashboardPaging.Page(dashboard, new(
            ProductId: "ALPHA",
            ClientSearch: "pc0",
            ProductSearch: "alp",
            State: PatchWorkflowState.Failed,
            InstallationStatus: "installed",
            Page: 1,
            PageSize: 10,
            SortColumn: "client",
            SortDirection: "desc"));

        Assert.Equal(49, filtered.Total);
        Assert.Equal(105, filtered.SnapshotTotal);
        Assert.Equal(10, filtered.Items.Count);
        Assert.Equal("pc098", filtered.Items[0].Client.ClientId);

        PatchClientStatePage bounded = PatchDashboardPaging.Page(dashboard, new(
            Page: 1,
            PageSize: 500,
            SortColumn: "client",
            SortDirection: "asc"));

        Assert.Equal(100, bounded.PageSize);
        Assert.Equal(100, bounded.Items.Count);
        Assert.Equal("pc001", bounded.Items[0].Client.ClientId);
    }

    [Fact]
    public void Page_UsesAllowlistedSortAndDeterministicTieBreakers()
    {
        PatchDashboardResult dashboard = Dashboard([
            Client("pc010", PatchWorkflowState.Completed, installedVersion: "2.0"),
            Client("pc002", PatchWorkflowState.Completed, installedVersion: "10.0"),
            Client("pc001", PatchWorkflowState.Completed, installedVersion: "1.0"),
        ]);

        PatchClientStatePage descending = PatchDashboardPaging.Page(dashboard, new(
            SortColumn: "installed",
            SortDirection: "desc"));
        PatchClientStatePage unknown = PatchDashboardPaging.Page(dashboard, new(
            SortColumn: "not-allowed",
            SortDirection: "asc"));

        Assert.Equal(["pc010", "pc002", "pc001"],
            descending.Items.Select(item => item.Client.ClientId));
        Assert.Equal(["pc001", "pc002", "pc010"],
            unknown.Items.Select(item => item.Client.ClientId));
    }

    private static PatchDashboardResult Dashboard(List<PatchClientState> clients)
    {
        var product = new PatchProductRow(
            "alpha",
            "Alpha Product",
            "2.0-1",
            "2.0-1",
            null,
            "NOT_CONFIGURED",
            null,
            null,
            false,
            [new("depot-a", "2.0-1")],
            [],
            PatchPackageStatus.Current,
            PatchWorkflowState.Completed,
            clients.Count,
            clients.Count(client => client.State == PatchWorkflowState.UpdateAvailable),
            clients.Count(client => client.State == PatchWorkflowState.Failed),
            clients.Count(client => client.State == PatchWorkflowState.RolloutRequested),
            null,
            clients,
            [],
            []);
        return new PatchDashboardResult(
            "https://opsi.example:4447/",
            "depot-a",
            new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero),
            new PatchDashboardSummary(
                1, 0, 0, 0, 0, 0, 0, clients.Count, 1, 0),
            [new("depot-a", "Depot A", true, clients.Count)],
            [product],
            []);
    }

    private static PatchClientState Client(
        string clientId,
        PatchWorkflowState state,
        string installedVersion = "1.0") => new(
            clientId,
            "depot-a",
            installedVersion,
            "2.0",
            "installed",
            "none",
            state == PatchWorkflowState.Failed ? "failed" : "successful",
            state);
}
