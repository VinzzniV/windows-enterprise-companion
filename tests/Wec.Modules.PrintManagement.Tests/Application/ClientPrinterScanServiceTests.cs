using Microsoft.Extensions.Options;
using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.PrintManagement.Application;
using Wec.Modules.PrintManagement.Domain;

namespace Wec.Modules.PrintManagement.Tests.Application;

public sealed class ClientPrinterScanServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 15, 0, 0, TimeSpan.Zero);

    private readonly IWmiQueryService _wmiQueryService = Substitute.For<IWmiQueryService>();

    private ClientPrinterScanService CreateService()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        return new ClientPrinterScanService(_wmiQueryService, clock, Options.Create(new RemoteScanOptions()));
    }

    private static WmiInstance Printer(
        string name, string? driver = null, string? port = null, string? location = null,
        object? shared = null, object? network = null) =>
        new(new Dictionary<string, object?>
        {
            ["Name"] = name,
            ["DriverName"] = driver,
            ["PortName"] = port,
            ["Location"] = location,
            ["Shared"] = shared,
            ["Network"] = network,
        });

    private void SetUpPrinters(params WmiInstance[] printers) =>
        _wmiQueryService.QueryAsync(
                Arg.Any<ScanTarget>(), Arg.Any<ScanCredentials>(), Arg.Any<ConnectionOptions>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<WmiInstance>>(printers));

    [Fact]
    public async Task Capture_MapsLocalAndNetworkPrinters()
    {
        SetUpPrinters(
            Printer(@"\\PRSRV\KF-NETPRT001", driver: "Kyocera KX", port: @"\\PRSRV\KF-NETPRT001"),
            Printer("Microsoft Print to PDF", driver: "Microsoft Print To PDF", port: "PORTPROMPT:"),
            Printer("Reception", shared: true));

        Result<ClientPrinterScan> result = await CreateService().CaptureAsync(
            ScanTarget.Local, ScanCredentials.CurrentUser, CancellationToken.None);

        Assert.True(result.IsSuccess);
        IReadOnlyList<ClientPrinter> printers = result.Value.Printers;
        Assert.Equal(3, printers.Count);

        ClientPrinter network = Assert.Single(printers, p => p.IsNetwork);
        Assert.Equal(@"\\PRSRV\KF-NETPRT001", network.Name);
        Assert.Equal("Kyocera KX", network.DriverName);

        Assert.Contains(printers, p => p is { Name: "Reception", Shared: true, IsNetwork: false });
        Assert.Contains(printers, p => p is { Name: "Microsoft Print to PDF", IsNetwork: false });
    }

    [Fact]
    public async Task Capture_PrefersNetworkPropertyOverNameHeuristic()
    {
        SetUpPrinters(
            Printer("Reception", network: true), // local-looking name, but MSFT says network
            Printer(@"\\PRSRV\legacy", network: false)); // UNC name, but MSFT says local

        Result<ClientPrinterScan> result = await CreateService().CaptureAsync(
            ScanTarget.Local, ScanCredentials.CurrentUser, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Printers.Single(p => p.Name == "Reception").IsNetwork);
        Assert.False(result.Value.Printers.Single(p => p.Name == @"\\PRSRV\legacy").IsNetwork);
    }

    [Fact]
    public async Task Capture_PropagatesQueryFailure()
    {
        _wmiQueryService.QueryAsync(
                Arg.Any<ScanTarget>(), Arg.Any<ScanCredentials>(), Arg.Any<ConnectionOptions>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<WmiInstance>>(
                new Error(ErrorCode.WinRmUnavailable, "WinRM not reachable")));

        Result<ClientPrinterScan> result = await CreateService().CaptureAsync(
            ScanTarget.Local, ScanCredentials.CurrentUser, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.WinRmUnavailable, result.Error!.Code);
    }
}
