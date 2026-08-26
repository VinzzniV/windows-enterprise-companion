using System.Text.Json;
using Wec.Core.Messaging;
using Wec.Host.Bridge;

namespace Wec.Host.Tests.Bridge;

public sealed class BridgeExecutionTimeoutPolicyTests
{
    private readonly BridgeExecutionTimeoutPolicy _policy = new();

    [Theory]
    [InlineData("targets", "list", 9)]
    [InlineData("printmanagement", "scanServer", 30)]
    [InlineData("security", "runScan", 115)]
    [InlineData("patchmanagement", "searchWingetPackages", 205)]
    [InlineData("patchmanagement", "previewWingetPackage", 205)]
    [InlineData("patchmanagement", "checkWingetUpdates", 590)]
    [InlineData("employeelifecycle", "getHygiene", 175)]
    [InlineData("employeelifecycle", "getHygieneOverview", 175)]
    [InlineData("employeelifecycle", "listHygieneDevices", 175)]
    [InlineData("employeelifecycle", "listClientWorkspace", 175)]
    [InlineData("security", "runBatchScan", 590)]
    [InlineData("logs", "recent", 25)]
    public void Resolve_ReturnsTheCentralActionLifetime(
        string module,
        string action,
        int expectedSeconds)
    {
        Assert.Equal(
            TimeSpan.FromSeconds(expectedSeconds),
            _policy.Resolve(new BridgeRequest("id", module, action, null)));
    }

    [Fact]
    public void WingetPackageExecution_ScalesBySelectedPackageCount()
    {
        JsonElement payload = JsonSerializer.SerializeToElement(new
        {
            packages = new[] { new { opsiProductId = "7zip" }, new { opsiProductId = "firefox" } },
        });

        TimeSpan timeout = _policy.Resolve(new BridgeRequest(
            "id",
            "patchmanagement",
            "applyWingetUpdates",
            payload));

        Assert.Equal(TimeSpan.FromSeconds(3_780), timeout);
    }
}
