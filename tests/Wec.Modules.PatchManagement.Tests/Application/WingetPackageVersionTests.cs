using Wec.Core.Opsi;
using Wec.Modules.PatchManagement.Application;

namespace Wec.Modules.PatchManagement.Tests.Application;

public sealed class WingetPackageVersionTests
{
    [Fact]
    public void NewProductVersion_StartsAtPackageVersionOne()
    {
        Assert.Equal(1, WingetPackageService.DeterminePackageVersion(null, "26.02"));
        Assert.Equal(1, WingetPackageService.DeterminePackageVersion(
            new OpsiProductOnDepot("7zip", "depot", "25.01", "7"), "26.02"));
    }

    [Fact]
    public void SameProductVersion_IncrementsPackageVersionForTemplateRebuild()
    {
        Assert.Equal(4, WingetPackageService.DeterminePackageVersion(
            new OpsiProductOnDepot("7zip", "depot", "26.02", "3"), "26.02"));
    }

    [Theory]
    [InlineData("25.01", "26.02", -1)]
    [InlineData("26.02", "26.02", 0)]
    [InlineData("26.2", "26.02", 0)]
    [InlineData("126.0", "26.999", 1)]
    public void DotVersionComparison_IsNumericAndProtectsDirection(
        string left,
        string right,
        int expectedSign)
    {
        Assert.Equal(expectedSign, Math.Sign(WingetPackageService.CompareDotVersions(left, right)));
    }
}
