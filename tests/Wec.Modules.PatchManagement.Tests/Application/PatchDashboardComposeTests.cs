using Wec.Core.Opsi;
using Wec.Modules.PatchManagement.Application;
using Wec.Modules.PatchManagement.Domain;
using Wec.Modules.PatchManagement.Persistence;

namespace Wec.Modules.PatchManagement.Tests.Application;

public sealed class PatchDashboardComposeTests
{
    private const string Depot = "depot-test.example.test";
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 8, 0, 0, TimeSpan.Zero);

    private static PatchDashboardResult Compose(string installedProductVersion = "25.01") =>
        PatchDashboardService.Compose(
            "https://opsi.example.test:4447/",
            Depot,
            [new OpsiDepot(Depot, "Test", true)],
            [new OpsiClientHost("client1.example.test", null, Depot, Now)],
            [
                new OpsiProduct("7zip", "7-Zip", "25.01", "1", null),
                new OpsiProduct("critical-app", "Critical App", "4.0", "1", null),
            ],
            [
                new OpsiProductOnDepot("7zip", Depot, "25.01", "1"),
                new OpsiProductOnDepot("critical-app", Depot, "4.0", "1"),
            ],
            [new OpsiProductOnClient("7zip", "client1.example.test", "installed", "none", "successful", installedProductVersion, "1", Now)],
            [new WingetManagedPackage(
                1, "7zip", "7zip.7zip", "winget", "machine", Depot, "7-Zip",
                "25.01", 1, "26.02", "SUCCESS", Now, null, Now, Now)],
            Now);

    [Fact]
    public void WingetVersionNewerThanDepot_IsMarkedAsAvailable()
    {
        PatchDashboardResult dashboard = Compose();

        PatchProductRow sevenZip = Assert.Single(dashboard.Products, row => row.ProductId == "7zip");
        Assert.True(sevenZip.WingetManaged);
        Assert.Equal("7zip.7zip", sevenZip.WingetId);
        Assert.True(sevenZip.WingetUpdateAvailable);
        Assert.Equal(PatchPackageStatus.UpdateAvailable, sevenZip.PackageStatus);
        Assert.Equal(1, dashboard.Summary.WingetManagedCount);
        Assert.Equal(1, dashboard.Summary.WingetUpdatesAvailable);
    }

    [Fact]
    public void UnmanagedProduct_RemainsManualAndVisible()
    {
        PatchDashboardResult dashboard = Compose();

        PatchProductRow manual = Assert.Single(dashboard.Products, row => row.ProductId == "critical-app");
        Assert.False(manual.WingetManaged);
        Assert.Equal("MANUAL", manual.WingetCheckStatus);
        Assert.Null(manual.WingetId);
    }

    [Fact]
    public void ClientState_IsReadOnlyVersionComparison()
    {
        PatchProductRow sevenZip = Assert.Single(Compose("24.08").Products, row => row.ProductId == "7zip");

        PatchClientState client = Assert.Single(sevenZip.Clients);
        Assert.Equal(PatchWorkflowState.UpdateAvailable, client.State);
        Assert.Equal("25.01-1", client.TargetVersion);
    }
}
