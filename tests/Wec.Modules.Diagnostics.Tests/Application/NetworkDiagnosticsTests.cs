using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Diagnostics.Application;
using Wec.Modules.Diagnostics.Application.Diagnostics;
using Wec.Modules.Diagnostics.Domain;

namespace Wec.Modules.Diagnostics.Tests.Application;

internal static class TestDefaults
{
    public static readonly DateTimeOffset Now = new(2026, 7, 2, 17, 0, 0, TimeSpan.Zero);

    public static IClock Clock()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        return clock;
    }

    public static Microsoft.Extensions.Options.IOptions<DiagnosticsOptions> Options(
        string dnsProbeHostname = "cloudflare.com") =>
        Microsoft.Extensions.Options.Options.Create(new DiagnosticsOptions
        {
            DnsProbeHostname = dnsProbeHostname,
            ProbeTimeout = TimeSpan.FromSeconds(3),
        });

    public static NetworkAdapterInfo Adapter(
        string name = "Ethernet",
        string[]? gateways = null,
        string[]? dnsServers = null) => new(
        name,
        $"{name} adapter",
        [new Ipv4AddressInfo("192.168.1.10", 24)],
        gateways ?? ["192.168.1.1"],
        dnsServers ?? ["192.168.1.1"]);

    public static void SetUpAdapters(this INetworkInfoProvider provider, params NetworkAdapterInfo[] adapters) =>
        provider.GetActiveAdapters().Returns(Result.Success<IReadOnlyList<NetworkAdapterInfo>>(adapters));
}

public class NetworkConfigurationDiagnosticTests
{
    private readonly INetworkInfoProvider _provider = Substitute.For<INetworkInfoProvider>();

    private NetworkConfigurationDiagnostic CreateDiagnostic() => new(_provider, TestDefaults.Clock());

    [Fact]
    public async Task ProviderFailure_ProducesNotRunResult()
    {
        _provider.GetActiveAdapters().Returns(Result.Failure<IReadOnlyList<NetworkAdapterInfo>>(
            new Error(ErrorCode.NetworkProbeFailed, "stack error")));

        DiagnosticResult result = Assert.Single(await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));
        Assert.Equal(DiagnosticStatus.NotRun, result.Status);
    }

    [Fact]
    public async Task NoActiveAdapters_ProducesFailWithNextSteps()
    {
        _provider.SetUpAdapters();

        DiagnosticResult result = Assert.Single(await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));
        Assert.Equal(DiagnosticStatus.Fail, result.Status);
        Assert.NotEmpty(result.SuggestedNextSteps);
    }

    [Fact]
    public async Task AdapterWithoutGateway_ProducesWarning()
    {
        _provider.SetUpAdapters(TestDefaults.Adapter(gateways: []));

        DiagnosticResult result = Assert.Single(await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));
        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.Contains(result.SuggestedNextSteps, step => step.Contains("gateway", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CompleteConfiguration_ProducesPassWithAdapterEvidence()
    {
        _provider.SetUpAdapters(TestDefaults.Adapter());

        DiagnosticResult result = Assert.Single(await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));
        Assert.Equal(DiagnosticStatus.Pass, result.Status);
        Assert.Contains("192.168.1.10/24", result.Evidence["adapter: Ethernet"], StringComparison.Ordinal);
    }
}

public class GatewayReachabilityDiagnosticTests
{
    private readonly INetworkInfoProvider _provider = Substitute.For<INetworkInfoProvider>();
    private readonly IPingProbe _pingProbe = Substitute.For<IPingProbe>();

    private GatewayReachabilityDiagnostic CreateDiagnostic() =>
        new(_provider, _pingProbe, TestDefaults.Options(), TestDefaults.Clock());

    private void SetUpProbeReply(bool success, long roundtripMs, string status) =>
        _pingProbe
            .SendAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new PingProbeReply(success, roundtripMs, status)));

    [Fact]
    public async Task NoGateway_ProducesNotRun_WithoutPinging()
    {
        _provider.SetUpAdapters(TestDefaults.Adapter(gateways: []));

        DiagnosticResult result = Assert.Single(await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));
        Assert.Equal(DiagnosticStatus.NotRun, result.Status);
        await _pingProbe.DidNotReceive()
            .SendAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GatewayReplies_ProducesPassWithRoundtripEvidence()
    {
        _provider.SetUpAdapters(TestDefaults.Adapter());
        SetUpProbeReply(success: true, roundtripMs: 2, status: "Success");

        DiagnosticResult result = Assert.Single(await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));
        Assert.Equal(DiagnosticStatus.Pass, result.Status);
        Assert.Equal("2", result.Evidence["roundtripMs"]);
    }

    [Fact]
    public async Task GatewayTimeout_ProducesWarning_WithIcmpHintInNextSteps()
    {
        _provider.SetUpAdapters(TestDefaults.Adapter());
        SetUpProbeReply(success: false, roundtripMs: 0, status: "TimedOut");

        DiagnosticResult result = Assert.Single(await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));
        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.Contains(result.SuggestedNextSteps, step => step.Contains("ICMP", StringComparison.Ordinal));
        Assert.Contains(result.SuggestedNextSteps, step => step.Contains("default route", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.SuggestedNextSteps, step => step.Contains("another device", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LocalProbeError_ProducesFail()
    {
        _provider.SetUpAdapters(TestDefaults.Adapter());
        _pingProbe
            .SendAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<PingProbeReply>(new Error(
                ErrorCode.NetworkProbeFailed, "invalid gateway address")));

        DiagnosticResult result = Assert.Single(await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));
        Assert.Equal(DiagnosticStatus.Fail, result.Status);
    }
}

public class DnsResolutionDiagnosticTests
{
    private readonly IDnsResolver _dnsResolver = Substitute.For<IDnsResolver>();

    private DnsResolutionDiagnostic CreateDiagnostic(string probeHostname = "cloudflare.com") =>
        new(_dnsResolver, TestDefaults.Options(probeHostname), TestDefaults.Clock());

    [Fact]
    public async Task SuccessfulResolution_ProducesPassWithAddresses()
    {
        _dnsResolver
            .ResolveAsync("cloudflare.com", Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<string>>(["104.16.132.229", "104.16.133.229"]));

        DiagnosticResult result = Assert.Single(await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));
        Assert.Equal(DiagnosticStatus.Pass, result.Status);
        Assert.Contains("104.16.132.229", result.Evidence["addresses"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolutionFailure_ProducesFailWithNextSteps()
    {
        _dnsResolver
            .ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<string>>(new Error(
                ErrorCode.NetworkProbeFailed, "no such host")));

        DiagnosticResult result = Assert.Single(await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));
        Assert.Equal(DiagnosticStatus.Fail, result.Status);
        Assert.NotEmpty(result.SuggestedNextSteps);
    }

    [Fact]
    public async Task ConfiguredProbeHostname_IsUsedInsteadOfAnyHardcodedTarget()
    {
        _dnsResolver
            .ResolveAsync("intranet.contoso.example", Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<string>>(["10.0.0.5"]));

        DiagnosticResult result = Assert.Single(
            await CreateDiagnostic("intranet.contoso.example").EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));

        Assert.Equal(DiagnosticStatus.Pass, result.Status);
        await _dnsResolver.Received(1).ResolveAsync("intranet.contoso.example", Arg.Any<CancellationToken>());
    }
}

public class DiagnosticRunServiceTests
{
    [Fact]
    public async Task CrashingDiagnostic_BecomesVisibleFailResult_AndOthersStillRun()
    {
        var crashing = Substitute.For<IDiagnostic>();
        crashing.DiagnosticId.Returns("CRASHING");
        crashing.EvaluateAsync(Arg.Any<DiagnosticContext>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("bug"));

        var healthy = Substitute.For<IDiagnostic>();
        healthy.DiagnosticId.Returns("HEALTHY");
        healthy.EvaluateAsync(Arg.Any<DiagnosticContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<DiagnosticResult>>([new DiagnosticResult(
                "HEALTHY", "ok", DiagnosticStatus.Pass, DiagnosticCategory.Network, "x",
                new Dictionary<string, string>(), [], null, TestDefaults.Now)]));

        var service = new DiagnosticRunService(
            [crashing, healthy], TestDefaults.Clock(), NullLogger<DiagnosticRunService>.Instance);

        Result<DiagnosticRunResult> run = await service.RunAsync(DiagnosticContext.Local, CancellationToken.None);

        Assert.True(run.IsSuccess);
        Assert.Equal(2, run.Value.Results.Count);
        Assert.Contains(run.Value.Results, result =>
            result.DiagnosticId == "CRASHING" && result.Status == DiagnosticStatus.Fail);
        Assert.Contains(run.Value.Results, result =>
            result.DiagnosticId == "HEALTHY" && result.Status == DiagnosticStatus.Pass);
    }
}
