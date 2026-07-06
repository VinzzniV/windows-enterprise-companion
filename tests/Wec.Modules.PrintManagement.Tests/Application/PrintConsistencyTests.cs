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
        DeviceQueryError? deviceError = null) =>
        new(queue, null, driver, driverVersion, null, address, location, comment, null, deviceError);

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
    public void DefaultPublicCommunity_IsFlagged()
    {
        IReadOnlyList<PrintHint> hints = PrintConsistency.ComputeHints([], "public");

        Assert.Single(hints, h => h.Category == "Default SNMP community");
        Assert.Empty(PrintConsistency.ComputeHints([], "internal-ro"));
    }
}
