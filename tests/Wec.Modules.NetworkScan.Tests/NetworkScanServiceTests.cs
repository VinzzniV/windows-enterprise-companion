using Microsoft.Extensions.Options;
using NSubstitute;
using Wec.Core.Dhcp;
using Wec.Core.Network;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.NetworkScan;
using Wec.Modules.NetworkScan.Application;
using Wec.Modules.NetworkScan.Domain;
using Wec.Modules.NetworkScan.Handlers;

namespace Wec.Modules.NetworkScan.Tests;

public sealed class NetworkScanServiceTests
{
    private readonly INetworkScanner _scanner = Substitute.For<INetworkScanner>();
    private readonly IDhcpReader _dhcpReader = Substitute.For<IDhcpReader>();

    private NetworkScanService CreateService() =>
        new(_scanner, _dhcpReader, Options.Create(new NetworkScanOptions()));

    private void ScannerReturns(params ScannedHost[] hosts) =>
        _scanner.ScanAsync(Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<int>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<ScannedHost>>(hosts));

    private void ReservationsReturn(params DhcpReservation[] reservations) =>
        _dhcpReader.GetReservationsAsync(Arg.Any<string>(), Arg.Any<ScanCredentials>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<DhcpReservation>>(reservations));

    private static ScannedHost Live(string ip) =>
        new(ip, IsUp: true, Hostname: null, MacAddress: null, MacVendor: null, []);

    [Fact]
    public async Task PolicyHandler_ExposesTheConfiguredPortScope()
    {
        var options = new NetworkScanOptions { ScanPorts = [80, 443, 9100] };
        var handler = new GetNetworkScanPolicyHandler(Options.Create(options));

        Result<NetworkScanPolicyResult> result = await handler.HandleAsync(
            new GetNetworkScanPolicyRequest(), CancellationToken.None);

        Assert.Equal([80, 443, 9100], result.Value.ScanPorts);
    }

    [Fact]
    public async Task WithoutDhcp_DoesDiscoveryOnly_AndDoesNotFlagReservations()
    {
        ScannerReturns(Live("172.20.20.10"));

        Result<NetworkScanResult> result = await CreateService().ScanAsync(
            "172.20.20.0/24", scanPorts: false, dhcp: null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.DhcpChecked);
        NetworkHostRow row = Assert.Single(result.Value.Hosts);
        Assert.False(row.HasReservation);
        await _dhcpReader.DidNotReceiveWithAnyArgs()
            .GetReservationsAsync(Arg.Any<string>(), Arg.Any<ScanCredentials>(), Arg.Any<CancellationToken>());
        await _scanner.Received().ScanAsync(
            Arg.Any<string?>(), Arg.Any<string>(),
            Arg.Is<IReadOnlyList<int>>(ports => ports.Count == 0), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LiveHostWithReservation_IsOk_WithoutReservation_IsRogue()
    {
        ScannerReturns(Live("172.20.20.10"), Live("172.20.20.11"));
        ReservationsReturn(new DhcpReservation("172.20.20.10", "00-11-22", "PK-NETPRT010"));

        Result<NetworkScanResult> result = await CreateService().ScanAsync(
            "172.20.20.0/24", scanPorts: true,
            new DhcpQuery("dhcp01", ScanCredentials.CurrentUser), CancellationToken.None);

        Assert.True(result.Value.DhcpChecked);
        NetworkHostRow reserved = Assert.Single(result.Value.Hosts, host => host.Ip == "172.20.20.10");
        Assert.True(reserved.HasReservation);
        Assert.Equal("PK-NETPRT010", reserved.ReservationName);
        NetworkHostRow rogue = Assert.Single(result.Value.Hosts, host => host.Ip == "172.20.20.11");
        Assert.False(rogue.HasReservation);
    }

    [Fact]
    public async Task ReservedButDeadIpInRange_SurfacesAsStaleRow()
    {
        ScannerReturns(Live("172.20.20.10"));
        ReservationsReturn(
            new DhcpReservation("172.20.20.10", null, "alive"),
            new DhcpReservation("172.20.20.99", null, "dead-in-range"),
            new DhcpReservation("10.9.9.9", null, "dead-out-of-range"));

        Result<NetworkScanResult> result = await CreateService().ScanAsync(
            "172.20.20.0/24", scanPorts: false,
            new DhcpQuery("dhcp01", ScanCredentials.CurrentUser), CancellationToken.None);

        NetworkHostRow stale = Assert.Single(result.Value.Hosts, host => !host.IsUp);
        Assert.Equal("172.20.20.99", stale.Ip);
        Assert.True(stale.HasReservation);
        Assert.DoesNotContain(result.Value.Hosts, host => host.Ip == "10.9.9.9");
    }

    [Fact]
    public async Task ScannerFailure_IsPropagated()
    {
        _scanner.ScanAsync(Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<int>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<ScannedHost>>(new Error(ErrorCode.ServiceUnavailable, "nmap missing")));

        Result<NetworkScanResult> result = await CreateService().ScanAsync(
            "172.20.20.0/24", scanPorts: false, dhcp: null, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.ServiceUnavailable, result.Error!.Code);
    }
}
