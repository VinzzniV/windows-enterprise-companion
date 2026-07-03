using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Diagnostics.Application.Diagnostics;
using Wec.Modules.Diagnostics.Domain;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Wec.Modules.Diagnostics.Tests.Application;

internal static class DiagnosticsTestSetup
{
    public static readonly DateTimeOffset Now = new(2026, 7, 3, 9, 0, 0, TimeSpan.Zero);

    public static IClock Clock()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        return clock;
    }

    public static Microsoft.Extensions.Options.IOptions<DiagnosticsOptions> Options() =>
        MsOptions.Create(new DiagnosticsOptions { DnsProbeHostname = "probe.example" });
}

public class DiskFreeSpaceDiagnosticTests
{
    private readonly IDriveInfoProvider _driveInfoProvider = Substitute.For<IDriveInfoProvider>();

    private DiskFreeSpaceDiagnostic CreateDiagnostic() => new(
        _driveInfoProvider, DiagnosticsTestSetup.Options(), DiagnosticsTestSetup.Clock());

    [Fact]
    public async Task DriveBelowThreshold_ProducesWarning()
    {
        _driveInfoProvider.GetFixedDrives().Returns(Result.Success<IReadOnlyList<DriveSpaceInfo>>(
        [
            new DriveSpaceInfo(@"C:\", TotalBytes: 100_000_000_000, AvailableFreeBytes: 5_000_000_000),
            new DriveSpaceInfo(@"D:\", TotalBytes: 100_000_000_000, AvailableFreeBytes: 50_000_000_000),
        ]));

        DiagnosticResult result = Assert.Single(
            await CreateDiagnostic().EvaluateAsync(CancellationToken.None));

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.Contains(@"C:\", result.Title, StringComparison.Ordinal);
        Assert.Equal(DiagnosticCategory.System, result.Category);
    }

    [Fact]
    public async Task AllDrivesAboveThreshold_ProducesPass()
    {
        _driveInfoProvider.GetFixedDrives().Returns(Result.Success<IReadOnlyList<DriveSpaceInfo>>(
        [
            new DriveSpaceInfo(@"C:\", TotalBytes: 100_000_000_000, AvailableFreeBytes: 40_000_000_000),
        ]));

        DiagnosticResult result = Assert.Single(
            await CreateDiagnostic().EvaluateAsync(CancellationToken.None));

        Assert.Equal(DiagnosticStatus.Pass, result.Status);
    }
}

public class DnsServerReachabilityDiagnosticTests
{
    private readonly INetworkInfoProvider _networkInfoProvider = Substitute.For<INetworkInfoProvider>();
    private readonly IPingProbe _pingProbe = Substitute.For<IPingProbe>();

    private DnsServerReachabilityDiagnostic CreateDiagnostic() => new(
        _networkInfoProvider, _pingProbe, DiagnosticsTestSetup.Options(), DiagnosticsTestSetup.Clock());

    private void SetUpDnsServers(params string[] servers) =>
        _networkInfoProvider.GetActiveAdapters().Returns(Result.Success<IReadOnlyList<NetworkAdapterInfo>>(
        [
            new NetworkAdapterInfo("Ethernet", "Intel", [], [], [.. servers]),
        ]));

    [Fact]
    public async Task ReachableServer_ProducesPassWithRoundtrip()
    {
        SetUpDnsServers("10.0.0.10", "10.0.0.11");
        _pingProbe.SendAsync("10.0.0.10", Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new PingProbeReply(true, 3, "Success")));
        _pingProbe.SendAsync("10.0.0.11", Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new PingProbeReply(false, 0, "TimedOut")));

        DiagnosticResult result = Assert.Single(
            await CreateDiagnostic().EvaluateAsync(CancellationToken.None));

        Assert.Equal(DiagnosticStatus.Pass, result.Status);
        Assert.Equal(DiagnosticCategory.Dns, result.Category);
        Assert.Contains("1 of 2", result.Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoServerAnswers_ProducesWarningNotFail()
    {
        SetUpDnsServers("10.0.0.10");
        _pingProbe.SendAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new PingProbeReply(false, 0, "TimedOut")));

        DiagnosticResult result = Assert.Single(
            await CreateDiagnostic().EvaluateAsync(CancellationToken.None));

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
    }

    [Fact]
    public async Task NoDnsServersConfigured_ProducesWarning()
    {
        SetUpDnsServers();

        DiagnosticResult result = Assert.Single(
            await CreateDiagnostic().EvaluateAsync(CancellationToken.None));

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
    }
}

public class DomainControllerReachabilityDiagnosticTests
{
    private readonly IWmiQueryService _wmiQueryService = Substitute.For<IWmiQueryService>();
    private readonly IDnsResolver _dnsResolver = Substitute.For<IDnsResolver>();
    private readonly IPingProbe _pingProbe = Substitute.For<IPingProbe>();

    private DomainControllerReachabilityDiagnostic CreateDiagnostic() => new(
        _wmiQueryService, _dnsResolver, _pingProbe, DiagnosticsTestSetup.Options(), DiagnosticsTestSetup.Clock());

    private void SetUpComputerSystem(bool partOfDomain, string domain) =>
        _wmiQueryService.QueryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<WmiInstance>>(
            [
                new WmiInstance(new Dictionary<string, object?>
                {
                    ["PartOfDomain"] = partOfDomain,
                    ["Domain"] = domain,
                }),
            ]));

    [Fact]
    public async Task WorkgroupMachine_SkipsWithPass()
    {
        SetUpComputerSystem(partOfDomain: false, domain: "WORKGROUP");

        DiagnosticResult result = Assert.Single(
            await CreateDiagnostic().EvaluateAsync(CancellationToken.None));

        Assert.Equal(DiagnosticStatus.Pass, result.Status);
        await _dnsResolver.DidNotReceive().ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DomainNotResolvable_ProducesFail()
    {
        SetUpComputerSystem(partOfDomain: true, domain: "contoso.local");
        _dnsResolver.ResolveAsync("contoso.local", Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<string>>(new Error(
                ErrorCode.NetworkProbeFailed, "no such domain")));

        DiagnosticResult result = Assert.Single(
            await CreateDiagnostic().EvaluateAsync(CancellationToken.None));

        Assert.Equal(DiagnosticStatus.Fail, result.Status);
        Assert.Equal(DiagnosticCategory.Domain, result.Category);
    }

    [Fact]
    public async Task ResolvableAndPingable_ProducesPass()
    {
        SetUpComputerSystem(partOfDomain: true, domain: "contoso.local");
        _dnsResolver.ResolveAsync("contoso.local", Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<string>>(["10.0.0.1"]));
        _pingProbe.SendAsync("10.0.0.1", Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new PingProbeReply(true, 2, "Success")));

        DiagnosticResult result = Assert.Single(
            await CreateDiagnostic().EvaluateAsync(CancellationToken.None));

        Assert.Equal(DiagnosticStatus.Pass, result.Status);
    }

    [Fact]
    public async Task ResolvableButNoPingAnswer_ProducesWarning()
    {
        SetUpComputerSystem(partOfDomain: true, domain: "contoso.local");
        _dnsResolver.ResolveAsync("contoso.local", Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<string>>(["10.0.0.1"]));
        _pingProbe.SendAsync("10.0.0.1", Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new PingProbeReply(false, 0, "TimedOut")));

        DiagnosticResult result = Assert.Single(
            await CreateDiagnostic().EvaluateAsync(CancellationToken.None));

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
    }
}

public class NetworkAdapterClassificationTests
{
    private static NetworkAdapterInfo Adapter(string name, string description) =>
        new(name, description, [], [], []);

    [Theory]
    [InlineData("vEthernet (Default Switch)", "Hyper-V Virtual Ethernet Adapter")]
    [InlineData("Ethernet 2", "VMware Virtual Ethernet Adapter for VMnet8")]
    [InlineData("wg0", "WireGuard Tunnel")]
    public void VirtualAdapters_AreClassifiedAsVirtual(string name, string description)
    {
        Assert.True(NetworkConfigurationDiagnostic.IsVirtualAdapter(Adapter(name, description)));
    }

    [Theory]
    [InlineData("Ethernet", "Intel(R) Ethernet Connection I219-LM")]
    [InlineData("WLAN", "Intel(R) Wi-Fi 6E AX211 160MHz")]
    public void PhysicalAdapters_AreClassifiedAsPhysical(string name, string description)
    {
        Assert.False(NetworkConfigurationDiagnostic.IsVirtualAdapter(Adapter(name, description)));
    }

    [Fact]
    public async Task VirtualAdaptersNextToPhysical_GetASeparateSecondaryResult()
    {
        var networkInfoProvider = Substitute.For<INetworkInfoProvider>();
        networkInfoProvider.GetActiveAdapters().Returns(Result.Success<IReadOnlyList<NetworkAdapterInfo>>(
        [
            new NetworkAdapterInfo(
                "Ethernet", "Intel(R) Ethernet", [new Ipv4AddressInfo("10.0.0.5", 24)],
                ["10.0.0.1"], ["10.0.0.10"], "AA:BB:CC:DD:EE:FF", 1_000_000_000, true, "Ethernet"),
            new NetworkAdapterInfo("vEthernet (WSL)", "Hyper-V Virtual Ethernet Adapter", [], [], []),
        ]));
        var diagnostic = new NetworkConfigurationDiagnostic(networkInfoProvider, DiagnosticsTestSetup.Clock());

        IReadOnlyList<DiagnosticResult> results = await diagnostic.EvaluateAsync(CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Equal(DiagnosticStatus.Pass, results[0].Status);
        Assert.Contains("MAC: AA:BB:CC:DD:EE:FF", results[0].Evidence["adapter: Ethernet"], StringComparison.Ordinal);
        Assert.Contains("1000 Mbit/s", results[0].Evidence["adapter: Ethernet"], StringComparison.Ordinal);
        Assert.Contains("DHCP", results[0].Evidence["adapter: Ethernet"], StringComparison.Ordinal);
        Assert.Equal("Virtual network adapters", results[1].AffectedResource);
    }
}
