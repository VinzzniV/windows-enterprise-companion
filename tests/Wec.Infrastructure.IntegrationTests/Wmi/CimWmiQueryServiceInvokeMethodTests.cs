using Microsoft.Extensions.Logging.Abstractions;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Infrastructure.Wmi;

namespace Wec.Infrastructure.IntegrationTests.Wmi;

public sealed class CimWmiQueryServiceInvokeMethodTests
{
    /// <summary>
    /// Drives the real StdRegProv provider on the local machine: proves the
    /// method-invoke path (parameter marshaling, out-parameter mapping) that
    /// the remote software inventory depends on.
    /// </summary>
    [Fact]
    public async Task EnumKey_OnLocalUninstallKey_ReturnsSubKeyNames()
    {
        var service = new CimWmiQueryService(NullLogger<CimWmiQueryService>.Instance);

        Result<WmiInstance> result = await service.InvokeMethodAsync(
            ScanTarget.Local,
            ScanCredentials.CurrentUser,
            ConnectionOptions.Default,
            @"root\default",
            "StdRegProv",
            "EnumKey",
            new Dictionary<string, object?>
            {
                ["hDefKey"] = 0x80000002u,
                ["sSubKeyName"] = @"SOFTWARE\Microsoft\Windows\CurrentVersion",
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(0, result.Value.GetInteger("ReturnValue"));
        string[]? subKeyNames = result.Value.GetRawValue("sNames") as string[];
        Assert.NotNull(subKeyNames);
        Assert.NotEmpty(subKeyNames);
    }
}
