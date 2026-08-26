using Microsoft.Management.Deployment;
using Wec.Infrastructure.SoftwareUpdates;

namespace Wec.Infrastructure.IntegrationTests.SoftwareUpdates;

public sealed class ComWingetCatalogClientTests
{
    [Fact]
    public async Task SearchAsync_UsesTheLiveWingetCatalogWhenExplicitlyEnabled()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("WEC_RUN_LIVE_WINGET_TESTS"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        ComWingetCatalogClient client = new();
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2));

        var exact = await client.GetExactAsync("7zip.7zip", timeout.Token);
        Assert.True(exact.IsSuccess, exact.Error?.Message);
        Assert.Equal("7zip.7zip", exact.Value.Id, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("winget", exact.Value.Source, StringComparer.OrdinalIgnoreCase);

        var fuzzy = await client.SearchAsync("7-Zip", 10, timeout.Token);
        Assert.True(fuzzy.IsSuccess, fuzzy.Error?.Message);
        Assert.Contains(fuzzy.Value, package =>
            string.Equals(package.Id, "7zip.7zip", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SearchOptions_DistinguishFuzzyCatalogSearchFromExactId()
    {
        FindPackagesOptions fuzzy = ComWingetCatalogClient.BuildSearchOptions("7-Zip", 25, false);
        PackageMatchFilter fuzzyFilter = Assert.Single(fuzzy.Selectors);
        Assert.Equal(PackageMatchField.CatalogDefault, fuzzyFilter.Field);
        Assert.Equal(PackageFieldMatchOption.ContainsCaseInsensitive, fuzzyFilter.Option);

        FindPackagesOptions exact = ComWingetCatalogClient.BuildSearchOptions("7zip.7zip", 1, true);
        PackageMatchFilter exactFilter = Assert.Single(exact.Selectors);
        Assert.Equal(PackageMatchField.Id, exactFilter.Field);
        Assert.Equal(PackageFieldMatchOption.EqualsCaseInsensitive, exactFilter.Option);
    }

    [Theory]
    [InlineData(PackageInstallerType.MSStore)]
    [InlineData(PackageInstallerType.Portable)]
    [InlineData(PackageInstallerType.Zip)]
    [InlineData(PackageInstallerType.Font)]
    public void Eligibility_RejectsUnsupportedInstallerTypes(PackageInstallerType installerType)
    {
        ComWingetCatalogClient.WingetInstallerEligibility result =
            ComWingetCatalogClient.AssessInstaller(
                true, installerType, PackageInstallerScope.System, ElevationRequirement.ElevationRequired);

        Assert.False(result.Eligible);
        Assert.Contains("not supported", result.Reason);
    }

    [Fact]
    public void Eligibility_RejectsUserScopeAndAcceptsMachineMsi()
    {
        Assert.False(ComWingetCatalogClient.AssessInstaller(
            true, PackageInstallerType.Msi, PackageInstallerScope.User,
            ElevationRequirement.ElevationProhibited).Eligible);
        Assert.True(ComWingetCatalogClient.AssessInstaller(
            true, PackageInstallerType.Msi, PackageInstallerScope.System,
            ElevationRequirement.ElevationRequired).Eligible);
    }
}
