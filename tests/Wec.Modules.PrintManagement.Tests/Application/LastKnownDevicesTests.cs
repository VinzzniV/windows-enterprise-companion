using Wec.Modules.PrintManagement.Application;
using Wec.Modules.PrintManagement.Domain;

namespace Wec.Modules.PrintManagement.Tests.Application;

public sealed class LastKnownDevicesTests
{
    private static readonly DateTimeOffset Earlier = new(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 7, 3, 8, 0, 0, TimeSpan.Zero);

    private static PrinterEntry Entry(
        string queueName, string? address, PrinterDevice? device, DeviceQueryError? error = null) =>
        new(queueName, null, null, null, null, address, null, null, device, error);

    private static PrinterDevice Device() =>
        new("VCF1234567", "UTAX P-4539i MFP", "PR-EG", "Denkingen", "Idle", 1000,
            [new TonerSupply("Toner Black", 80, false)]);

    [Fact]
    public void Fill_UnreachableDevice_KeepsIdentityFromEarlierScanWithoutVolatileValues()
    {
        var older = new PrintServerSnapshot("PRSRV", Earlier, [Entry("Queue-A", "10.1.1.20", Device())]);
        var latest = new PrintServerSnapshot("PRSRV", Now,
            [Entry("Queue-A", "10.1.1.20", null, new DeviceQueryError("CONNECTION_TIMEOUT", "no answer"))]);

        PrinterEntry filled = Assert.Single(LastKnownDevices.Fill(latest, older).Printers);

        Assert.Equal("VCF1234567", filled.Device!.SerialNumber);
        Assert.Equal("UTAX P-4539i MFP", filled.Device.Model);
        Assert.Equal("Denkingen", filled.Device.SysLocation);
        Assert.Equal(Earlier, filled.DeviceDataFromUtc);
        // The device is still unreachable — status, toner and page count are not carried over
        Assert.Null(filled.Device.Status);
        Assert.Null(filled.Device.PageCount);
        Assert.Empty(filled.Device.Supplies);
        Assert.Equal("CONNECTION_TIMEOUT", filled.DeviceError!.Code);
    }

    [Fact]
    public void Fill_RenamedQueue_MatchesOnThePortAddress()
    {
        var older = new PrintServerSnapshot("PRSRV", Earlier, [Entry("Old-Name", "10.1.1.20", Device())]);
        var latest = new PrintServerSnapshot("PRSRV", Now,
            [Entry("New-Name", "10.1.1.20", null, new DeviceQueryError("CONNECTION_TIMEOUT", "no answer"))]);

        PrinterEntry filled = Assert.Single(LastKnownDevices.Fill(latest, older).Printers);

        Assert.Equal("VCF1234567", filled.Device!.SerialNumber);
    }

    [Fact]
    public void Fill_FreshData_IsNeverOverwritten()
    {
        var older = new PrintServerSnapshot("PRSRV", Earlier, [Entry("Queue-A", "10.1.1.20", Device())]);
        PrinterDevice swapped = Device() with { SerialNumber = "NEW-SERIAL" };
        var latest = new PrintServerSnapshot("PRSRV", Now, [Entry("Queue-A", "10.1.1.20", swapped)]);

        PrinterEntry filled = Assert.Single(LastKnownDevices.Fill(latest, older).Printers);

        Assert.Equal("NEW-SERIAL", filled.Device!.SerialNumber);
        Assert.Null(filled.DeviceDataFromUtc);
    }

    [Fact]
    public void Fill_QueueWithoutPortAddress_StaysUntouched()
    {
        var older = new PrintServerSnapshot("PRSRV", Earlier, [Entry("Queue-A", "10.1.1.20", Device())]);
        var latest = new PrintServerSnapshot("PRSRV", Now, [Entry("Queue-A", null, null)]);

        PrinterEntry filled = Assert.Single(LastKnownDevices.Fill(latest, older).Printers);

        Assert.Null(filled.Device);
        Assert.Null(filled.DeviceDataFromUtc);
    }
}
