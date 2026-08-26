using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Diagnostics.Application;
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
    private readonly IWmiQueryService _wmiQueryService = Substitute.For<IWmiQueryService>();

    private DiskFreeSpaceDiagnostic CreateDiagnostic() => new(
        _wmiQueryService, DiagnosticsTestSetup.Options(), DiagnosticsTestSetup.Clock());

    private void SetUpLogicalDisks(params (string Name, ulong Total, ulong Free)[] disks) =>
        _wmiQueryService.QueryAsync(
                Arg.Any<Wec.Core.Targets.ScanTarget>(),
                Arg.Any<Wec.Core.Targets.ScanCredentials>(),
                Arg.Any<Wec.Core.Targets.ConnectionOptions>(),
                Arg.Any<string>(),
                Arg.Is<string>(query => query.Contains("Win32_LogicalDisk", StringComparison.Ordinal)),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<WmiInstance>>([.. disks.Select(disk =>
                new WmiInstance(new Dictionary<string, object?>
                {
                    ["DeviceID"] = disk.Name,
                    ["Size"] = disk.Total,
                    ["FreeSpace"] = disk.Free,
                }))]));

    [Fact]
    public async Task DriveBelowThreshold_ProducesWarning()
    {
        SetUpLogicalDisks(("C:", 100_000_000_000, 5_000_000_000), ("D:", 100_000_000_000, 50_000_000_000));

        DiagnosticResult result = Assert.Single(
            await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));

        Assert.Equal("WEC-DIAG-SYS-DISKSPACE", result.DiagnosticId);
        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.Contains("C:", result.Title, StringComparison.Ordinal);
        Assert.Equal(DiagnosticCategory.System, result.Category);
    }

    [Fact]
    public async Task AllDrivesAboveThreshold_ProducesPass()
    {
        SetUpLogicalDisks(("C:", 100_000_000_000, 40_000_000_000));

        DiagnosticResult result = Assert.Single(
            await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));

        Assert.Equal(DiagnosticStatus.Pass, result.Status);
    }

    [Fact]
    public async Task RemoteTarget_QueriesTheRemoteMachine()
    {
        var remoteContext = new Wec.Modules.Diagnostics.Application.DiagnosticContext(
            Wec.Core.Targets.ScanTarget.Remote("pc-042"),
            Wec.Core.Targets.ScanCredentials.CurrentUser,
            Wec.Core.Targets.ConnectionOptions.Default);
        SetUpLogicalDisks(("C:", 100_000_000_000, 40_000_000_000));

        DiagnosticResult result = Assert.Single(
            await CreateDiagnostic().EvaluateAsync(remoteContext, CancellationToken.None));

        Assert.Equal(DiagnosticStatus.Pass, result.Status);
        await _wmiQueryService.Received(1).QueryAsync(
            Arg.Is<Wec.Core.Targets.ScanTarget>(target => target.Host == "pc-042"),
            Arg.Any<Wec.Core.Targets.ScanCredentials>(),
            Arg.Any<Wec.Core.Targets.ConnectionOptions>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }
}

public class WindowsUpdateRecencyDiagnosticTests
{
    private readonly IWmiQueryService _wmiQueryService = Substitute.For<IWmiQueryService>();

    private WindowsUpdateRecencyDiagnostic CreateDiagnostic(int maximumAgeInDays = 60) => new(
        _wmiQueryService,
        MsOptions.Create(new DiagnosticsOptions
        {
            DnsProbeHostname = "probe.example",
            MaxDaysSinceLastInstalledUpdate = maximumAgeInDays,
        }),
        DiagnosticsTestSetup.Clock());

    private void SetUpHotfixes(params (string Id, string? InstalledOn)[] hotfixes) =>
        _wmiQueryService.QueryAsync(
                Arg.Any<Wec.Core.Targets.ScanTarget>(),
                Arg.Any<Wec.Core.Targets.ScanCredentials>(),
                Arg.Any<Wec.Core.Targets.ConnectionOptions>(),
                Arg.Any<string>(),
                Arg.Is<string>(query => query.Contains("Win32_QuickFixEngineering", StringComparison.Ordinal)),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<WmiInstance>>([.. hotfixes.Select(hotfix =>
                new WmiInstance(new Dictionary<string, object?>
                {
                    ["HotFixID"] = hotfix.Id,
                    ["InstalledOn"] = hotfix.InstalledOn,
                }))]));

    [Fact]
    public async Task RecentUpdate_ProducesPassWithStableContractEvidence()
    {
        SetUpHotfixes(("KB5070001", "2026-06-15"), ("KB5060001", "2026-01-10"));

        DiagnosticResult result = Assert.Single(
            await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));

        Assert.Equal("WEC-DIAG-SYS-UPDATES", result.DiagnosticId);
        Assert.Equal(DiagnosticStatus.Pass, result.Status);
        Assert.Equal(DiagnosticCategory.System, result.Category);
        Assert.Equal("KB5070001", result.Evidence["hotfix"]);
        Assert.Equal("2026-06-15", result.Evidence["lastInstalledUpdate"]);
        Assert.Equal("18", result.Evidence["daysSinceLastUpdate"]);
    }

    [Fact]
    public async Task UpdateOlderThanThreshold_ProducesWarning()
    {
        SetUpHotfixes(("KB5060001", "2026-01-10"));

        DiagnosticResult result = Assert.Single(
            await CreateDiagnostic(maximumAgeInDays: 60)
                .EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.NotEmpty(result.SuggestedNextSteps);
    }

    [Fact]
    public async Task MissingParseableDates_ProducesNotRun()
    {
        SetUpHotfixes(("KB5070001", null), ("KB5060001", "not-a-date"));

        DiagnosticResult result = Assert.Single(
            await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));

        Assert.Equal(DiagnosticStatus.NotRun, result.Status);
        Assert.Equal("2", result.Evidence["hotfixCount"]);
    }

    [Fact]
    public async Task WmiFailure_ProducesNotRun()
    {
        _wmiQueryService.QueryAsync(
                Arg.Any<Wec.Core.Targets.ScanTarget>(),
                Arg.Any<Wec.Core.Targets.ScanCredentials>(),
                Arg.Any<Wec.Core.Targets.ConnectionOptions>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<WmiInstance>>(Error.WmiUnavailable("unreachable")));

        DiagnosticResult result = Assert.Single(
            await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));

        Assert.Equal(DiagnosticStatus.NotRun, result.Status);
        Assert.Equal("unreachable", result.Evidence["errorMessage"]);
    }

    [Fact]
    public async Task RemoteTarget_QueriesTheRemoteMachine()
    {
        var remoteContext = new DiagnosticContext(
            Wec.Core.Targets.ScanTarget.Remote("pc-042"),
            Wec.Core.Targets.ScanCredentials.CurrentUser,
            Wec.Core.Targets.ConnectionOptions.Default);
        SetUpHotfixes(("KB5070001", "2026-06-15"));

        DiagnosticResult result = Assert.Single(
            await CreateDiagnostic().EvaluateAsync(remoteContext, CancellationToken.None));

        Assert.Equal(DiagnosticStatus.Pass, result.Status);
        await _wmiQueryService.Received(1).QueryAsync(
            Arg.Is<Wec.Core.Targets.ScanTarget>(target => target.Host == "pc-042"),
            Arg.Any<Wec.Core.Targets.ScanCredentials>(),
            Arg.Any<Wec.Core.Targets.ConnectionOptions>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }
}

public class RemoteDiagnosticsTests
{
    private static readonly Wec.Modules.Diagnostics.Application.DiagnosticContext RemoteContext = new(
        Wec.Core.Targets.ScanTarget.Remote("pc-042"),
        Wec.Core.Targets.ScanCredentials.CurrentUser,
        Wec.Core.Targets.ConnectionOptions.Default);

    [Fact]
    public async Task LocalPerspectiveChecks_AreVisiblySkippedForRemoteTargets()
    {
        var networkInfoProvider = Substitute.For<INetworkInfoProvider>();
        var diagnostic = new NetworkConfigurationDiagnostic(networkInfoProvider, DiagnosticsTestSetup.Clock());

        DiagnosticResult result = Assert.Single(
            await diagnostic.EvaluateAsync(RemoteContext, CancellationToken.None));

        Assert.Equal(DiagnosticStatus.NotRun, result.Status);
        Assert.Equal("UnsupportedRemoteOperation", result.Evidence["errorCode"]);
        Assert.Equal("pc-042", result.Evidence["target"]);
        // The local probe must not run against the wrong machine
        networkInfoProvider.DidNotReceive().GetActiveAdapters();
    }

    [Fact]
    public async Task RebootPending_RemoteTarget_ReadsSignalsThroughStdRegProv()
    {
        var registryReader = Substitute.For<IRegistryReader>();
        var wmiQueryService = Substitute.For<IWmiQueryService>();
        wmiQueryService.InvokeMethodAsync(
                Arg.Any<Wec.Core.Targets.ScanTarget>(),
                Arg.Any<Wec.Core.Targets.ScanCredentials>(),
                Arg.Any<Wec.Core.Targets.ConnectionOptions>(),
                Arg.Any<string>(),
                Arg.Is("StdRegProv"),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object?>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var parameters = callInfo.Arg<IReadOnlyDictionary<string, object?>>();
                bool isRebootPendingKey = ((string?)parameters["sSubKeyName"])?.EndsWith(
                    "RebootPending", StringComparison.Ordinal) == true;
                return Result.Success(new WmiInstance(new Dictionary<string, object?>
                {
                    // Only the CBS RebootPending key exists on the fake target
                    ["ReturnValue"] = isRebootPendingKey ? 0u : 2u,
                }));
            });
        var diagnostic = new RebootPendingDiagnostic(
            registryReader, wmiQueryService, DiagnosticsTestSetup.Clock());

        DiagnosticResult result = Assert.Single(
            await diagnostic.EvaluateAsync(RemoteContext, CancellationToken.None));

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.Contains("Component Based Servicing", result.Evidence["signals"], StringComparison.Ordinal);
        // The local registry seam must not be touched for a remote target
        registryReader.DidNotReceiveWithAnyArgs().ReadLocalMachineSubKeyNames(default!);
    }

    [Fact]
    public async Task RebootPending_RemoteTransportFailure_BecomesNotRunWithError()
    {
        var wmiQueryService = Substitute.For<IWmiQueryService>();
        wmiQueryService.InvokeMethodAsync(
                Arg.Any<Wec.Core.Targets.ScanTarget>(),
                Arg.Any<Wec.Core.Targets.ScanCredentials>(),
                Arg.Any<Wec.Core.Targets.ConnectionOptions>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object?>>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Failure<WmiInstance>(new Error(
                ErrorCode.WinRmUnavailable, "WinRM on 'pc-042' is not reachable.")));
        var diagnostic = new RebootPendingDiagnostic(
            Substitute.For<IRegistryReader>(), wmiQueryService, DiagnosticsTestSetup.Clock());

        DiagnosticResult result = Assert.Single(
            await diagnostic.EvaluateAsync(RemoteContext, CancellationToken.None));

        Assert.Equal(DiagnosticStatus.NotRun, result.Status);
        Assert.Equal("WinRmUnavailable", result.Evidence["errorCode"]);
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
            new NetworkAdapterInfo(
                "Ethernet", "Intel", [new Ipv4AddressInfo("10.0.0.5", 24)], [], [.. servers]),
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
            await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));

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
            await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
    }

    [Fact]
    public async Task NoDnsServersConfigured_ProducesWarning()
    {
        SetUpDnsServers();

        DiagnosticResult result = Assert.Single(
            await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));

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
            await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));

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
            await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));

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
            await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));

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
            await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));

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
    [InlineData("Npcap Loopback Adapter", "Npcap Packet Driver")]
    [InlineData("Ethernet QoS", "Microsoft QoS Packet Scheduler")]
    [InlineData("WFP Adapter", "Windows Filtering Platform filter")]
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
    public async Task FilterAdaptersNextToRelevantAdapter_AreCollapsedIntoSecondaryEvidence()
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

        IReadOnlyList<DiagnosticResult> results = await diagnostic.EvaluateAsync(DiagnosticContext.Local, CancellationToken.None);

        DiagnosticResult result = Assert.Single(results);
        Assert.Equal(DiagnosticStatus.Pass, result.Status);
        Assert.Contains("MAC: AA:BB:CC:DD:EE:FF", result.Evidence["adapter: Ethernet"], StringComparison.Ordinal);
        Assert.Contains("1000 Mbit/s", result.Evidence["adapter: Ethernet"], StringComparison.Ordinal);
        Assert.Contains("DHCP", result.Evidence["adapter: Ethernet"], StringComparison.Ordinal);
        Assert.Equal("1", result.Evidence["secondaryAdapterCount"]);
        Assert.Contains("vEthernet (WSL)", result.Evidence["secondaryAdapters"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreferredRoutedVpn_RemainsRelevantDespiteVirtualMarker()
    {
        var networkInfoProvider = Substitute.For<INetworkInfoProvider>();
        networkInfoProvider.GetActiveAdapters().Returns(Result.Success<IReadOnlyList<NetworkAdapterInfo>>(
        [
            new NetworkAdapterInfo(
                "WireGuard", "WireGuard Tunnel", [new Ipv4AddressInfo("10.8.0.2", 24)],
                ["10.8.0.1"], ["10.8.0.1"], InterfaceIndex: 42, IsPreferredRoute: true),
        ]));
        var diagnostic = new NetworkConfigurationDiagnostic(networkInfoProvider, DiagnosticsTestSetup.Clock());

        DiagnosticResult result = Assert.Single(
            await diagnostic.EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));

        Assert.Equal(DiagnosticStatus.Pass, result.Status);
        Assert.Contains("adapter: WireGuard", result.Evidence.Keys);
        Assert.Equal("0", result.Evidence["secondaryAdapterCount"]);
    }
}
