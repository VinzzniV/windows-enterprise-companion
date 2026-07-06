using Wec.Modules.PrintManagement.Application;
using Wec.Modules.PrintManagement.Domain;

namespace Wec.Modules.PrintManagement.Tests.Application;

public sealed class LeaseDiffTests
{
    private static readonly DateTimeOffset BaselineTime = new(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LatestTime = new(2026, 7, 3, 8, 0, 0, TimeSpan.Zero);

    private static PrinterEntry Entry(string queue, string? serial, string? model = null, string? address = null) =>
        new(queue, null, null, null, null, address, null, null,
            serial is null ? null : new PrinterDevice(serial, model, null, null, null, null, []),
            null);

    [Fact]
    public void Compute_SplitsNewGoneAndSwapped()
    {
        var baseline = new PrintServerSnapshot("PRSRV1", BaselineTime,
        [
            Entry("Queue-A", "OLD-1", "TA 4020"),   // swapped at the same queue
            Entry("Queue-B", "KEEP-1"),             // unchanged
            Entry("Queue-C", "GONE-1", "TA 3206"),  // device (and queue) gone
        ]);
        var latest = new PrintServerSnapshot("PRSRV1", LatestTime,
        [
            Entry("Queue-A", "NEW-1", "UTAX P-4539i"),
            Entry("Queue-B", "KEEP-1"),
            Entry("Queue-D", "NEW-2", "UTAX 3207ci", "10.1.1.30"), // brand-new queue + device
        ]);

        PrintServerDiff diff = LeaseDiff.Compute(baseline, latest);

        Assert.Equal(BaselineTime, diff.BaselineAtUtc);
        Assert.Equal(LatestTime, diff.LatestAtUtc);

        LeaseQueueSwap swap = Assert.Single(diff.SwappedQueues);
        Assert.Equal("Queue-A", swap.QueueName);
        Assert.Equal("OLD-1", swap.OldSerialNumber);
        Assert.Equal("NEW-1", swap.NewSerialNumber);

        LeaseDiffDevice added = Assert.Single(diff.NewDevices);
        Assert.Equal("NEW-2", added.SerialNumber);
        Assert.Equal("10.1.1.30", added.DeviceAddress);

        LeaseDiffDevice gone = Assert.Single(diff.GoneDevices);
        Assert.Equal("GONE-1", gone.SerialNumber);
    }

    [Fact]
    public void Compute_CountsDevicesWhoseSerialCouldNotBeRead()
    {
        var baseline = new PrintServerSnapshot("PRSRV1", BaselineTime, [Entry("Queue-A", "SER-1")]);
        var latest = new PrintServerSnapshot("PRSRV1", LatestTime,
        [
            Entry("Queue-A", "SER-1"),
            Entry("Queue-B", serial: null, address: "10.1.1.40"), // unreachable device
        ]);

        PrintServerDiff diff = LeaseDiff.Compute(baseline, latest);

        Assert.Equal(1, diff.DevicesWithoutSerialNumber);
        Assert.Empty(diff.NewDevices);
        Assert.Empty(diff.GoneDevices);
        Assert.Empty(diff.SwappedQueues);
    }

    [Fact]
    public void Compute_UnchangedFleet_IsAnEmptyDiff()
    {
        var snapshot = new PrintServerSnapshot("PRSRV1", BaselineTime,
            [Entry("Queue-A", "SER-1"), Entry("Queue-B", "SER-2")]);

        PrintServerDiff diff = LeaseDiff.Compute(
            snapshot, snapshot with { CapturedAtUtc = LatestTime });

        Assert.Empty(diff.NewDevices);
        Assert.Empty(diff.GoneDevices);
        Assert.Empty(diff.SwappedQueues);
    }
}
