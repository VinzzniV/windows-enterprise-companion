using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.Security.Application;
using Wec.Modules.Security.Domain;
using Wec.Modules.Security.Persistence;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Wec.Modules.Security.Tests.Application;

public sealed class BatchSecurityScanServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 3, 10, 0, 0, TimeSpan.Zero);

    private readonly IWmiQueryService _wmiQueryService = Substitute.For<IWmiQueryService>();
    private readonly IBridgeEventPublisher _eventPublisher = Substitute.For<IBridgeEventPublisher>();
    private readonly ISecurityScanRepository _repository = Substitute.For<ISecurityScanRepository>();
    private readonly ISecurityCheck _check = Substitute.For<ISecurityCheck>();
    private readonly List<BridgeEvent> _publishedEvents = [];

    public BatchSecurityScanServiceTests()
    {
        _check.CheckId.Returns("TEST-CHECK");
        _check.EvaluateAsync(Arg.Any<SecurityScanContext>(), Arg.Any<CancellationToken>())
            .Returns(SecurityCheckResult.Succeeded("TEST-CHECK"));
        _repository.SaveScanAsync(
                Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(),
                Arg.Any<ScanStatus>(), Arg.Any<int>(), Arg.Any<IReadOnlyList<SecurityCheckResult>>(),
                Arg.Any<CancellationToken>())
            .Returns(1L);
        _eventPublisher
            .When(publisher => publisher.Publish(Arg.Any<BridgeEvent>()))
            .Do(call => { lock (_publishedEvents) { _publishedEvents.Add(call.Arg<BridgeEvent>()); } });
    }

    private BatchSecurityScanService CreateService(int maxParallelScans = 4)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        var services = new ServiceCollection();
        services.AddSingleton(_repository);
        services.AddSingleton<IEnumerable<ISecurityCheck>>([_check]);
        services.AddSingleton(clock);
        services.AddSingleton(MsOptions.Create(new RemoteScanOptions { MaxParallelScans = maxParallelScans }));
        services.AddSingleton(NullLogger<SecurityScanService>.Instance);
        services.AddScoped<SecurityScanService>(provider => new SecurityScanService(
            provider.GetRequiredService<IEnumerable<ISecurityCheck>>(),
            provider.GetRequiredService<ISecurityScanRepository>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<RemoteScanOptions>>(),
            NullLogger<SecurityScanService>.Instance));
        ServiceProvider serviceProvider = services.BuildServiceProvider();

        return new BatchSecurityScanService(
            _wmiQueryService,
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _eventPublisher,
            clock,
            MsOptions.Create(new RemoteScanOptions { MaxParallelScans = maxParallelScans }),
            NullLogger<BatchSecurityScanService>.Instance);
    }

    private void SetUpConnectivityGate(string host, Result<IReadOnlyList<WmiInstance>> result) =>
        _wmiQueryService.QueryAsync(
                Arg.Is<ScanTarget>(target => target.Host == host),
                Arg.Any<ScanCredentials>(),
                Arg.Any<ConnectionOptions>(),
                Arg.Any<string>(),
                Arg.Is<string>(query => query.Contains("Win32_OperatingSystem", StringComparison.Ordinal)),
                Arg.Any<CancellationToken>())
            .Returns(result);

    private static Result<IReadOnlyList<WmiInstance>> GateSuccess() =>
        Result.Success<IReadOnlyList<WmiInstance>>([new WmiInstance(new Dictionary<string, object?>())]);

    [Fact]
    public async Task EmptyHostList_IsInvalidRequest()
    {
        Result<BatchScanResult> result = await CreateService().RunBatchScanAsync(
            ["  ", ""], ScanCredentials.CurrentUser, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.InvalidRequest, result.Error!.Code);
    }

    [Fact]
    public async Task UnreachableHost_FailsThatHostOnly_BatchContinues()
    {
        SetUpConnectivityGate("pc-down", Result.Failure<IReadOnlyList<WmiInstance>>(new Error(
            ErrorCode.DnsResolutionFailed, "no such host")));
        SetUpConnectivityGate("pc-up", GateSuccess());

        Result<BatchScanResult> result = await CreateService().RunBatchScanAsync(
            ["pc-down", "pc-up"], ScanCredentials.CurrentUser, CancellationToken.None);

        Assert.True(result.IsSuccess);
        HostScanOutcome failed = result.Value.Hosts[0];
        HostScanOutcome succeeded = result.Value.Hosts[1];
        Assert.Equal(HostScanStatus.Failed, failed.Status);
        Assert.Null(failed.Scan);
        Assert.Equal(ScanPhase.Resolve, failed.Error!.Phase);
        Assert.Equal(ErrorCode.DnsResolutionFailed, failed.Error.Code);
        Assert.Equal(HostScanStatus.Completed, succeeded.Status);
        Assert.NotNull(succeeded.Scan);
        Assert.Equal("pc-up", succeeded.Scan.Host);
    }

    [Fact]
    public async Task DuplicateAndUntrimmedHosts_AreScannedOnce()
    {
        SetUpConnectivityGate("pc-01", GateSuccess());

        Result<BatchScanResult> result = await CreateService().RunBatchScanAsync(
            [" pc-01 ", "PC-01"], ScanCredentials.CurrentUser, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Hosts);
    }

    [Fact]
    public async Task CrashingCheck_MakesHostCompletedWithErrors_NotBatchFailure()
    {
        SetUpConnectivityGate("pc-01", GateSuccess());
        _check.EvaluateAsync(Arg.Any<SecurityScanContext>(), Arg.Any<CancellationToken>())
            .Returns<Task<SecurityCheckResult>>(_ => throw new InvalidOperationException("bug"));

        Result<BatchScanResult> result = await CreateService().RunBatchScanAsync(
            ["pc-01"], ScanCredentials.CurrentUser, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HostScanStatus.CompletedWithErrors, result.Value.Hosts[0].Status);
    }

    [Fact]
    public async Task ProgressEvents_CoverQueuedConnectingRunningAndTerminalState()
    {
        SetUpConnectivityGate("pc-01", GateSuccess());

        await CreateService().RunBatchScanAsync(["pc-01"], ScanCredentials.CurrentUser, CancellationToken.None);

        List<BridgeEvent> events;
        lock (_publishedEvents)
        {
            events = [.. _publishedEvents];
        }

        Assert.All(events, bridgeEvent =>
        {
            Assert.Equal("security", bridgeEvent.Module);
            Assert.Equal("batchScanProgress", bridgeEvent.EventName);
            Assert.Equal("pc-01", Assert.IsType<BatchScanProgress>(bridgeEvent.Payload).Host);
        });
        Assert.Equal(4, events.Count);
    }

    [Fact]
    public async Task ParallelLimitOne_StillScansAllHosts()
    {
        SetUpConnectivityGate("pc-01", GateSuccess());
        SetUpConnectivityGate("pc-02", GateSuccess());
        SetUpConnectivityGate("pc-03", GateSuccess());

        Result<BatchScanResult> result = await CreateService(maxParallelScans: 1).RunBatchScanAsync(
            ["pc-01", "pc-02", "pc-03"], ScanCredentials.CurrentUser, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Hosts.Count);
        Assert.All(result.Value.Hosts, outcome => Assert.Equal(HostScanStatus.Completed, outcome.Status));
    }
}
