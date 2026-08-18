using Wec.Core.Network;
using Wec.Modules.NetworkScan.Application;
using Wec.Modules.NetworkScan.Domain;

namespace Wec.Modules.NetworkScan.Tests;

public sealed class DeviceClassifierTests
{
    private static ScannedHost Host(IReadOnlyList<ScannedPort> ports, string? vendor = null) =>
        new("10.0.0.5", IsUp: true, Hostname: null, MacAddress: null, MacVendor: vendor, ports);

    [Fact]
    public void JetdirectPort_IsPrinter() =>
        Assert.Equal(DeviceKind.Printer, DeviceClassifier.Classify(Host([new ScannedPort(9100, null)])));

    [Fact]
    public void RdpPort_IsComputer() =>
        Assert.Equal(DeviceKind.Computer, DeviceClassifier.Classify(Host([new ScannedPort(3389, "ms-wbt-server")])));

    [Fact]
    public void SnmpPlusWeb_IsNetworkDevice() =>
        Assert.Equal(
            DeviceKind.NetworkDevice,
            DeviceClassifier.Classify(Host([new ScannedPort(161, "snmp"), new ScannedPort(80, "http")])));

    [Fact]
    public void PrinterPortWins_OverComputerVendor() =>
        Assert.Equal(
            DeviceKind.Printer,
            DeviceClassifier.Classify(Host([new ScannedPort(9100, null)], vendor: "Dell Inc.")));

    [Fact]
    public void EnterpriseVendor_ReadsAsNetwork_NotPrinter() =>
        Assert.Equal(
            DeviceKind.NetworkDevice,
            DeviceClassifier.Classify(Host([], vendor: "Hewlett Packard Enterprise")));

    [Fact]
    public void PrinterVendor_WithoutPorts_IsPrinter() =>
        Assert.Equal(DeviceKind.Printer, DeviceClassifier.Classify(Host([], vendor: "Canon Inc.")));

    [Fact]
    public void UnknownVendor_NoPorts_IsUnknown() =>
        Assert.Equal(DeviceKind.Unknown, DeviceClassifier.Classify(Host([], vendor: "Some Random Co.")));
}
