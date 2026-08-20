using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Infrastructure.Time;
using Wec.Infrastructure.Wmi;
using Wec.Modules.PrintManagement.Application;
using Wec.Modules.PrintManagement.Domain;

namespace Wec.Infrastructure.IntegrationTests.Wmi;

public sealed class ClientPrinterScanServiceLocalTests
{
    /// <summary>
    /// Drives the real local MSFT_Printer provider so unsupported projection
    /// properties cannot be hidden by the mocked module tests.
    /// </summary>
    [Fact]
    public async Task CaptureLocal_UsesProviderSupportedProjection()
    {
        var query = new CimWmiQueryService(NullLogger<CimWmiQueryService>.Instance);
        var service = new ClientPrinterScanService(
            query,
            new SystemClock(),
            Options.Create(new RemoteScanOptions()));

        Result<ClientPrinterScan> result = await service.CaptureAsync(
            ScanTarget.Local,
            ScanCredentials.CurrentUser,
            CancellationToken.None);

        Assert.True(result.IsSuccess, $"{result.Error?.Code}: {result.Error?.Message} {result.Error?.Details}");
        Assert.All(result.Value.Printers, printer => Assert.False(string.IsNullOrWhiteSpace(printer.Name)));
        Assert.All(
            result.Value.Printers.Where(printer => printer.Name.StartsWith(@"\\", StringComparison.Ordinal)),
            printer => Assert.True(printer.IsNetwork));
    }
}
