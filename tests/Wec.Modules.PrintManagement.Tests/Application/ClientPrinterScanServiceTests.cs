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
        object? shared = null, object? type = null) =>
        new(new Dictionary<string, object?>
        {
            ["Name"] = name,
            ["DriverName"] = driver,
            ["PortName"] = port,
            ["Location"] = location,
            ["Shared"] = shared,
            ["Type"] = type,
        });

    private void SetUpPrinters(params WmiInstance[] printers) =>
        _wmiQueryService.QueryAsync(
                Arg.Any<ScanTarget>(), Arg.Any<ScanCredentials>(), Arg.Any<ConnectionOptions>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<WmiInstance>>(printers));

    [Fact]
    public async Task Capture_UsesSupportedMsftPrinterProjection()
    {
        SetUpPrinters();

        Result<ClientPrinterScan> result = await CreateService().CaptureAsync(
            ScanTarget.Local, ScanCredentials.CurrentUser, CancellationToken.None);

        Assert.True(result.IsSuccess);
        await _wmiQueryService.Received(1).QueryAsync(
            Arg.Any<ScanTarget>(),
            Arg.Any<ScanCredentials>(),
            Arg.Any<ConnectionOptions>(),
            @"root\StandardCimv2",
            Arg.Is<string>(query => query.Contains(" Type FROM MSFT_Printer", StringComparison.Ordinal)
                                    && !query.Contains("Network", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

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
    public async Task Capture_PrefersTypePropertyOverNameHeuristic()
    {
        SetUpPrinters(
            Printer("Reception", type: 1u), // local-looking name, but MSFT says connection
            Printer(@"\\PRSRV\legacy", type: 0u)); // UNC name, but MSFT says local

        Result<ClientPrinterScan> result = await CreateService().CaptureAsync(
            ScanTarget.Local, ScanCredentials.CurrentUser, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Printers.Single(p => p.Name == "Reception").IsNetwork);
        Assert.False(result.Value.Printers.Single(p => p.Name == @"\\PRSRV\legacy").IsNetwork);
    }

    [Fact]
    public async Task Capture_ContextualizesWmiProviderFailure()
    {
        _wmiQueryService.QueryAsync(
                Arg.Any<ScanTarget>(), Arg.Any<ScanCredentials>(), Arg.Any<ConnectionOptions>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<WmiInstance>>(
                Error.WmiUnavailable("The WMI query failed.", "Provider detail")));

        Result<ClientPrinterScan> result = await CreateService().CaptureAsync(
            ScanTarget.Local, ScanCredentials.CurrentUser, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.WmiUnavailable, result.Error!.Code);
        Assert.Equal("Installed printers could not be read from the Windows PrintManagement provider.", result.Error.Message);
        Assert.Equal("Provider detail", result.Error.Details);
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
