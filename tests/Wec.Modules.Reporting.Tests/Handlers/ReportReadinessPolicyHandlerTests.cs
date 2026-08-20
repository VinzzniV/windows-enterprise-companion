using Microsoft.Extensions.Options;
using Wec.Modules.Reporting;
using Wec.Modules.Reporting.Handlers;

namespace Wec.Modules.Reporting.Tests.Handlers;

public sealed class ReportReadinessPolicyHandlerTests
{
    [Fact]
    public async Task HandleAsync_ReturnsConfiguredFreshnessWindowsInSeconds()
    {
        var options = Options.Create(new ReportingOptions
        {
            MaximumInventoryAge = TimeSpan.FromHours(12),
            MaximumSecurityScanAge = TimeSpan.FromMinutes(90),
        });
        var handler = new GetReportReadinessPolicyHandler(options);

        var result = await handler.HandleAsync(
            new GetReportReadinessPolicyRequest(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(43_200, result.Value.MaximumInventoryAgeSeconds);
        Assert.Equal(5_400, result.Value.MaximumSecurityScanAgeSeconds);
    }
}
