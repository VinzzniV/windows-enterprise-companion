using System.Text.Json;
using Wec.Core.Messaging;
using Wec.Host.Bridge;

namespace Wec.Host.Tests.Bridge;

public sealed class BridgeExecutionTimeoutPolicyTests
{
    private readonly BridgeExecutionTimeoutPolicy _policy = new();

    [Theory]
    [InlineData("targets", "list", 9)]
    [InlineData("security", "runScan", 115)]
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
    public void PackageExecution_ScalesByTargetDepotCount()
    {
        JsonElement payload = JsonSerializer.SerializeToElement(new
        {
            depotIds = new[] { "test", "production" },
        });

        TimeSpan timeout = _policy.Resolve(new BridgeRequest(
            "id",
            "patchmanagement",
            "executePackageUpdate",
            payload));

        Assert.Equal(TimeSpan.FromSeconds(3_780), timeout);
    }
}
