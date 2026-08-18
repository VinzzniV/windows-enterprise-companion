using Wec.Modules.PrintManagement.Application;
using Wec.Modules.PrintManagement.Domain;

namespace Wec.Modules.PrintManagement.Tests.Application;

public sealed class PrintConsistencyTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 3, 15, 0, 0, TimeSpan.Zero);

    private static PrinterEntry Entry(
        string queue,
        string? driver = null,
        string? driverVersion = null,
        string? address = null,
        string? location = "somewhere",
        string? comment = null,
        PrinterDevice? device = null,
        DeviceQueryError? deviceError = null) =>
        new(queue, null, driver, driverVersion, null, address, location, comment, device, deviceError);

    private static PrinterDevice DeviceAt(string? sysLocation) =>
        new(null, null, null, sysLocation, null, null, []);

    private static PrinterDevice DeviceModel(string model) =>
        new(null, model, null, null, null, null, []);

    [Theory]
    [InlineData("Kyocera ECOSYS M2040dn", "Kyocera ECOSYS M2040dn KX")]
    [InlineData("HP LaserJet MFP M428fdn", "HP LaserJet MFP M428 PCLm-S")]
    [InlineData("Anything", "HP Universal Printing PCL 6")]
    [InlineData("Generic Printer", "Some Driver Without Codes")]
    public void DriverMatchingModel_ProducesNoHint(string model, string driver)
    {
        var snapshot = new PrintServerSnapshot("PRSRV1", Now,
            [Entry("Queue-A", driver: driver, device: DeviceModel(model))]);

        IReadOnlyList<PrintHint> hints = PrintConsistency.ComputeHints([snapshot], "internal-ro");

        Assert.DoesNotContain(hints, h => h.Category == "Driver may not match model");
    }

    [Fact]
    public void DriverNotMatchingModelCode_IsFlagged()
    {
        var snapshot = new PrintServerSnapshot("PRSRV1", Now,
            [Entry("Queue-A", driver: "Kyocera FS-1041 KX", device: DeviceModel("Kyocera ECOSYS M2040dn"))]);

        IReadOnlyList<PrintHint> hints = PrintConsistency.ComputeHints([snapshot], "internal-ro");

        PrintHint hint = Assert.Single(hints, h => h.Category == "Driver may not match model");
        Assert.Contains("M2040dn", hint.Message, StringComparison.Ordinal);
        Assert.Contains("FS-1041", hint.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnusedDrivers_AreReportedPerServer()
    {
        var snapshot = new PrintServerSnapshot("PRSRV1", Now, [Entry("Queue-A")])
        {
            UnusedDrivers = [new UnusedDriver("Old Kyocera KX", "7.0.0.0"), new UnusedDriver("Dead HP PCL6", null)],
        };

        IReadOnlyList<PrintHint> hints = PrintConsistency.ComputeHints([snapshot], "internal-ro");

        PrintHint hint = Assert.Single(hints, h => h.Category == "Unused driver");
        Assert.Contains("Old Kyocera KX", hint.Message, StringComparison.Ordinal);
        Assert.Contains("Dead HP PCL6", hint.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnreachableDeviceWithAddress_BecomesOrphanedQueueHint()
    {
        var snapshot = new PrintServerSnapshot("PRSRV1", Now,
        [
            Entry("Queue-A", address: "10.1.1.20",
                deviceError: new DeviceQueryError("CONNECTION_TIMEOUT", "no answer")),
            Entry("Queue-B"),
        ]);

        IReadOnlyList<PrintHint> hints = PrintConsistency.ComputeHints([snapshot], "internal-ro");

        PrintHint hint = Assert.Single(hints, h => h.Category == "Unreachable device");
        Assert.Contains("Queue-A", hint.Message, StringComparison.Ordinal);
        Assert.Contains("10.1.1.20", hint.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DriverVersionSpreadAcrossServers_IsReported()
    {
        var serverOne = new PrintServerSnapshot("PRSRV1", Now,
            [Entry("Q1", driver: "Kyocera KX", driverVersion: "8.1.0.0")]);
        var serverTwo = new PrintServerSnapshot("PRSRV2", Now,
            [Entry("Q2", driver: "Kyocera KX", driverVersion: "8.4.0.0")]);

        IReadOnlyList<PrintHint> hints = PrintConsistency.ComputeHints([serverOne, serverTwo], "internal-ro");

        PrintHint hint = Assert.Single(hints, h => h.Category == "Driver version spread");
        Assert.Contains("8.1.0.0", hint.Message, StringComparison.Ordinal);
        Assert.Contains("8.4.0.0", hint.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void QueuesWithoutLocationAndComment_AreCountedWithExamples()
    {
        var snapshot = new PrintServerSnapshot("PRSRV1", Now,
        [
            Entry("Unlabeled", location: null, comment: null),
            Entry("Labeled", location: "EG", comment: null),
        ]);

        IReadOnlyList<PrintHint> hints = PrintConsistency.ComputeHints([snapshot], "internal-ro");

        PrintHint hint = Assert.Single(hints, h => h.Category == "Missing location/comment");
        Assert.Contains("Unlabeled", hint.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Labeled", hint.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeviceLocationDiffersFromPrintServer_IsFlaggedWithDeviceAsTruth()
    {
        var snapshot = new PrintServerSnapshot("PRSRV1", Now,
            [Entry("Queue-A", location: "EG Flur", device: DeviceAt("Denkingen 1. OG"))]);

        IReadOnlyList<PrintHint> hints = PrintConsistency.ComputeHints([snapshot], "internal-ro");

        PrintHint hint = Assert.Single(hints, h => h.Category == "Device location mismatch");
        Assert.Contains("EG Flur", hint.Message, StringComparison.Ordinal);
        Assert.Contains("Denkingen 1. OG", hint.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeviceWithoutLocation_IsFlaggedShowingPrintServerValue()
    {
        var snapshot = new PrintServerSnapshot("PRSRV1", Now,
            [Entry("Queue-A", location: "EG Flur", device: DeviceAt(null))]);

        IReadOnlyList<PrintHint> hints = PrintConsistency.ComputeHints([snapshot], "internal-ro");

        PrintHint hint = Assert.Single(hints, h => h.Category == "Device location missing");
        Assert.Contains("EG Flur", hint.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeviceLocationMatchesPrintServer_ProducesNoHint()
    {
        var snapshot = new PrintServerSnapshot("PRSRV1", Now,
            [Entry("Queue-A", location: "eg flur", device: DeviceAt("EG Flur"))]);

        IReadOnlyList<PrintHint> hints = PrintConsistency.ComputeHints([snapshot], "internal-ro");

        Assert.DoesNotContain(hints, h => h.Category.StartsWith("Device location", StringComparison.Ordinal));
    }

    [Fact]
    public void DefaultPublicCommunity_IsFlagged()
    {
        IReadOnlyList<PrintHint> hints = PrintConsistency.ComputeHints([], "public");

        Assert.Single(hints, h => h.Category == "Default SNMP community");
        Assert.Empty(PrintConsistency.ComputeHints([], "internal-ro"));
    }
}
