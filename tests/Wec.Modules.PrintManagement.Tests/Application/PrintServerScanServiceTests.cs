using Microsoft.Extensions.Options;
using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Core.Snmp;
using Wec.Core.Targets;
using Wec.Modules.PrintManagement;
using Wec.Modules.PrintManagement.Application;
using Wec.Modules.PrintManagement.Domain;

namespace Wec.Modules.PrintManagement.Tests.Application;

public sealed class PrintServerScanServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 3, 15, 0, 0, TimeSpan.Zero);

    private readonly IWmiQueryService _wmiQueryService = Substitute.For<IWmiQueryService>();
    private readonly ISnmpReader _snmpReader = Substitute.For<ISnmpReader>();

    private PrintServerScanService CreateService()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        return new PrintServerScanService(
            _wmiQueryService,
            _snmpReader,
            clock,
            Options.Create(new PrintManagementOptions()),
            Options.Create(new RemoteScanOptions()));
    }

    private void SetUpCim(
        IReadOnlyList<WmiInstance> printers,
        IReadOnlyList<WmiInstance> ports,
        IReadOnlyList<WmiInstance> drivers)
    {
        _wmiQueryService.QueryAsync(
                Arg.Any<ScanTarget>(), Arg.Any<ScanCredentials>(), Arg.Any<ConnectionOptions>(),
                Arg.Any<string>(), Arg.Is<string>(query => query.EndsWith("FROM MSFT_Printer")),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success(printers));
        _wmiQueryService.QueryAsync(
                Arg.Any<ScanTarget>(), Arg.Any<ScanCredentials>(), Arg.Any<ConnectionOptions>(),
                Arg.Any<string>(), Arg.Is<string>(query => query.Contains("MSFT_PrinterPort")),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success(ports));
        _wmiQueryService.QueryAsync(
                Arg.Any<ScanTarget>(), Arg.Any<ScanCredentials>(), Arg.Any<ConnectionOptions>(),
                Arg.Any<string>(), Arg.Is<string>(query => query.Contains("MSFT_PrinterDriver")),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success(drivers));
    }

    private static WmiInstance Instance(params (string Name, object? Value)[] properties) =>
        new(properties.ToDictionary(property => property.Name, property => property.Value));

    [Fact]
    public async Task Capture_MergesCimAndSnmpIntoPrinterEntries()
    {
        SetUpCim(
            printers:
            [
                Instance(("Name", "Denkingen-EG"), ("ShareName", "PR-EG"), ("DriverName", "Kyocera KX"),
                    ("PortName", "IP_10.1.1.20"), ("Location", "EG Flur"), ("Comment", "Leasing 2026")),
            ],
            ports: [Instance(("Name", "IP_10.1.1.20"), ("PrinterHostAddress", "10.1.1.20"))],
            drivers: [Instance(("Name", "Kyocera KX"), ("DriverVersion", 0x0008_0001_0000_0000L))]);

        _snmpReader.GetAsync(Arg.Any<SnmpEndpoint>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<SnmpVarBind>>(
            [
                new("1.3.6.1.2.1.43.5.1.1.17.1", SnmpValue.OfText("VCF1234567")),
                new("1.3.6.1.2.1.25.3.2.1.3.1", SnmpValue.OfText("UTAX P-4539i MFP")),
                new("1.3.6.1.2.1.1.5.0", SnmpValue.OfText("PR-DENKINGEN-EG")),
                new("1.3.6.1.2.1.1.6.0", SnmpValue.OfText("Denkingen EG")),
                new("1.3.6.1.2.1.25.3.5.1.1.1", SnmpValue.OfNumber(3)),
                new("1.3.6.1.2.1.43.10.2.1.4.1.1", SnmpValue.OfNumber(123456)),
            ]));
        _snmpReader.WalkAsync(Arg.Any<SnmpEndpoint>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<SnmpVarBind>>(
            [
                new("1.3.6.1.2.1.43.11.1.1.6.1.1", SnmpValue.OfText("Toner Black")),
                new("1.3.6.1.2.1.43.11.1.1.8.1.1", SnmpValue.OfNumber(10000)),
                new("1.3.6.1.2.1.43.11.1.1.9.1.1", SnmpValue.OfNumber(800)),
            ]));

        Result<PrintServerSnapshot> result = await CreateService().CaptureAsync(
            ScanTarget.Remote("prsrv-denkingen"), ScanCredentials.CurrentUser, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("PRSRV-DENKINGEN", result.Value.Server);
        Assert.Equal(Now, result.Value.CapturedAtUtc);
        PrinterEntry entry = Assert.Single(result.Value.Printers);
        Assert.Equal("Denkingen-EG", entry.QueueName);
        Assert.Equal("10.1.1.20", entry.DeviceAddress);
        // An address that is already an IP literal resolves to itself, no DNS needed
        Assert.Equal("10.1.1.20", entry.DeviceIp);
        Assert.NotNull(entry.Device);
        Assert.Equal("VCF1234567", entry.Device!.SerialNumber);
        Assert.Equal("UTAX P-4539i MFP", entry.Device.Model);
        Assert.Equal("Idle", entry.Device.Status);
        Assert.Equal(123456, entry.Device.PageCount);
        TonerSupply supply = Assert.Single(entry.Device.Supplies);
        Assert.Equal("Toner Black", supply.Description);
        Assert.Equal(8, supply.Percent);
        Assert.True(supply.IsLow);
        Assert.Null(entry.DeviceError);
    }

    [Fact]
    public async Task Capture_UnreachableDevice_BecomesPerPrinterErrorNotScanAbort()
    {
        SetUpCim(
            printers:
            [
                Instance(("Name", "Queue-A"), ("PortName", "IP_10.1.1.20")),
                Instance(("Name", "Queue-B"), ("PortName", "WSD-Port")),
            ],
            ports: [Instance(("Name", "IP_10.1.1.20"), ("PrinterHostAddress", "10.1.1.20"))],
            drivers: []);

        _snmpReader.GetAsync(Arg.Any<SnmpEndpoint>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<SnmpVarBind>>(new Error(
                ErrorCode.ConnectionTimeout, "'10.1.1.20' did not answer on UDP 161 within 3 seconds.")));

        Result<PrintServerSnapshot> result = await CreateService().CaptureAsync(
            ScanTarget.Local, ScanCredentials.CurrentUser, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Printers.Count);
        PrinterEntry unreachable = Assert.Single(result.Value.Printers, entry => entry.QueueName == "Queue-A");
        Assert.Null(unreachable.Device);
        Assert.Equal("CONNECTION_TIMEOUT", unreachable.DeviceError!.Code);
        // The WSD queue has no address — CIM-only row without a device error
        PrinterEntry wsd = Assert.Single(result.Value.Printers, entry => entry.QueueName == "Queue-B");
        Assert.Null(wsd.DeviceAddress);
        Assert.Null(wsd.DeviceError);
    }

    [Fact]
    public async Task Capture_ReportsTcpPortsNoQueueUsesAsUnused()
    {
        SetUpCim(
            printers: [Instance(("Name", "Queue-A"), ("PortName", "IP_10.1.1.20"))],
            ports:
            [
                Instance(("Name", "IP_10.1.1.20"), ("PrinterHostAddress", "10.1.1.20")),
                Instance(("Name", "IP_10.1.1.99"), ("PrinterHostAddress", "10.1.1.99")),
                Instance(("Name", "PORTPROMPT:")),
            ],
            drivers: []);
        _snmpReader.GetAsync(Arg.Any<SnmpEndpoint>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<SnmpVarBind>>(new Error(ErrorCode.ConnectionTimeout, "no answer")));

        Result<PrintServerSnapshot> result = await CreateService().CaptureAsync(
            ScanTarget.Remote("prsrv"), ScanCredentials.CurrentUser, CancellationToken.None);

        UnusedPort unused = Assert.Single(result.Value.UnusedPorts);
        Assert.Equal("IP_10.1.1.99", unused.Name);
        Assert.Equal("10.1.1.99", unused.HostAddress);
    }

    [Fact]
    public async Task Capture_PooledPortNames_AreNotReportedAsUnused()
    {
        SetUpCim(
            printers: [Instance(("Name", "Pool-A"), ("PortName", "PK-NETPRT008,PK-NETPRT039"))],
            ports:
            [
                Instance(("Name", "PK-NETPRT008"), ("PrinterHostAddress", "PK-NETPRT008")),
                Instance(("Name", "PK-NETPRT039"), ("PrinterHostAddress", "PK-NETPRT039")),
                Instance(("Name", "IP_10.1.1.99"), ("PrinterHostAddress", "10.1.1.99")),
            ],
            drivers: []);
        _snmpReader.GetAsync(Arg.Any<SnmpEndpoint>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<SnmpVarBind>>(new Error(ErrorCode.ConnectionTimeout, "no answer")));

        Result<PrintServerSnapshot> result = await CreateService().CaptureAsync(
            ScanTarget.Remote("prsrv"), ScanCredentials.CurrentUser, CancellationToken.None);

        UnusedPort unused = Assert.Single(result.Value.UnusedPorts);
        Assert.Equal("IP_10.1.1.99", unused.Name);
    }

    [Fact]
    public async Task Capture_IgnoredPseudoPrinters_AreNotCapturedAtAll()
    {
        SetUpCim(
            printers:
            [
                Instance(("Name", "Microsoft Print to PDF"), ("DriverName", "Microsoft Print To PDF"),
                    ("PortName", "PORTPROMPT:")),
                Instance(("Name", "Microsoft XPS Document Writer"), ("DriverName", "Microsoft XPS Document Writer v4"),
                    ("PortName", "XPSPort:")),
                Instance(("Name", "PDFCreator"), ("DriverName", "PDFCreator"), ("PortName", "pdfcmon")),
                Instance(("Name", "Queue-A"), ("PortName", "IP_10.1.1.20")),
            ],
            ports: [Instance(("Name", "IP_10.1.1.20"), ("PrinterHostAddress", "10.1.1.20"))],
            drivers:
            [
                Instance(("Name", "Microsoft Print To PDF")),
                Instance(("Name", "Microsoft XPS Document Writer v4")),
                Instance(("Name", "PDFCreator")),
            ]);
        _snmpReader.GetAsync(Arg.Any<SnmpEndpoint>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<SnmpVarBind>>(new Error(ErrorCode.ConnectionTimeout, "no answer")));

        Result<PrintServerSnapshot> result = await CreateService().CaptureAsync(
            ScanTarget.Remote("prsrv"), ScanCredentials.CurrentUser, CancellationToken.None);

        PrinterEntry entry = Assert.Single(result.Value.Printers);
        Assert.Equal("Queue-A", entry.QueueName);
        // Their drivers must not resurface as "unused" either
        Assert.Empty(result.Value.UnusedDrivers);
    }

    [Fact]
    public async Task Capture_CimFailure_Propagates()
    {
        _wmiQueryService.QueryAsync(
                Arg.Any<ScanTarget>(), Arg.Any<ScanCredentials>(), Arg.Any<ConnectionOptions>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<WmiInstance>>(new Error(
                ErrorCode.WinRmUnavailable, "WinRM unreachable")));

        Result<PrintServerSnapshot> result = await CreateService().CaptureAsync(
            ScanTarget.Remote("prsrv"), ScanCredentials.CurrentUser, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.WinRmUnavailable, result.Error!.Code);
    }

    [Theory]
    [InlineData(3, "Idle")]
    [InlineData(4, "Printing")]
    [InlineData(5, "Warmup")]
    [InlineData(null, null)]
    [InlineData(99, null)]
    public void MapPrinterStatus_CoversHostResourcesCodes(int? code, string? expected)
    {
        Assert.Equal(expected, PrintServerScanService.MapPrinterStatus(code));
    }

    [Fact]
    public void FormatDriverVersion_UnpacksFourUInt16Parts()
    {
        Assert.Equal("8.1.0.0", PrintServerScanService.FormatDriverVersion(0x0008_0001_0000_0000));
        Assert.Null(PrintServerScanService.FormatDriverVersion(null));
    }

    [Fact]
    public void ParseSupplies_UnknownAndSomeRemainingLevels_HaveNoPercent()
    {
        IReadOnlyList<TonerSupply> supplies = PrintServerScanService.ParseSupplies(
        [
            new("1.3.6.1.2.1.43.11.1.1.6.1.1", SnmpValue.OfText("Toner Black")),
            new("1.3.6.1.2.1.43.11.1.1.8.1.1", SnmpValue.OfNumber(10000)),
            new("1.3.6.1.2.1.43.11.1.1.9.1.1", SnmpValue.OfNumber(-3)),
            new("1.3.6.1.2.1.43.11.1.1.6.1.2", SnmpValue.OfText("Waste Toner Box")),
            new("1.3.6.1.2.1.43.11.1.1.8.1.2", SnmpValue.OfNumber(-2)),
            new("1.3.6.1.2.1.43.11.1.1.9.1.2", SnmpValue.OfNumber(-2)),
        ], lowThresholdPercent: 15);

        Assert.Equal(2, supplies.Count);
        Assert.All(supplies, supply => Assert.Null(supply.Percent));
        Assert.All(supplies, supply => Assert.False(supply.IsLow));
    }

    [Fact]
    public void ParseSupplies_ComputesPercentAndLowFlag()
    {
        IReadOnlyList<TonerSupply> supplies = PrintServerScanService.ParseSupplies(
        [
            new("1.3.6.1.2.1.43.11.1.1.6.1.1", SnmpValue.OfText("Toner Cyan")),
            new("1.3.6.1.2.1.43.11.1.1.8.1.1", SnmpValue.OfNumber(100)),
            new("1.3.6.1.2.1.43.11.1.1.9.1.1", SnmpValue.OfNumber(50)),
        ], lowThresholdPercent: 15);

        TonerSupply supply = Assert.Single(supplies);
        Assert.Equal(50, supply.Percent);
        Assert.False(supply.IsLow);
    }
}
