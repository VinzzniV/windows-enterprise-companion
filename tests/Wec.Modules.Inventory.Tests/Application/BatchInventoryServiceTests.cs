using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.Inventory.Application;
using Wec.Modules.Inventory.Domain;
using Wec.Modules.Inventory.Persistence;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Wec.Modules.Inventory.Tests.Application;

public sealed class BatchInventoryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 27, 8, 0, 0, TimeSpan.Zero);

    private readonly IWmiQueryService _wmiQueryService = Substitute.For<IWmiQueryService>();
    private readonly IHardwareSnapshotRepository _repository = Substitute.For<IHardwareSnapshotRepository>();
    private readonly IRegistryReader _registryReader = Substitute.For<IRegistryReader>();
    private readonly IBridgeEventPublisher _eventPublisher = Substitute.For<IBridgeEventPublisher>();
    private readonly List<BridgeEvent> _publishedEvents = [];
    private readonly CapturingLogger<BatchInventoryService> _batchLogger = new();

    public BatchInventoryServiceTests()
    {
        _eventPublisher
            .When(publisher => publisher.Publish(Arg.Any<BridgeEvent>()))
            .Do(call =>
            {
                lock (_publishedEvents)
                {
                    _publishedEvents.Add(call.Arg<BridgeEvent>());
                }
            });
        _repository.SaveAsync(
                Arg.Any<string>(),
                Arg.Any<HardwareSnapshot>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _wmiQueryService.InvokeMethodAsync(
                Arg.Any<ScanTarget>(),
                Arg.Any<ScanCredentials>(),
                Arg.Any<ConnectionOptions>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object?>>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Failure<WmiInstance>(new Error(
                ErrorCode.UnsupportedRemoteOperation,
                "Remote software inventory unavailable in this fixture.")));
    }

    [Fact]
    public async Task EmptyAndOversizedHostSets_AreRejectedBeforeScanning()
    {
        BatchInventoryService service = CreateService(maxBatchHosts: 2);

        Result<InventoryBatchResult> empty = await service.RunAsync(
            [" ", ""], ScanCredentials.CurrentUser, CancellationToken.None);
        Result<InventoryBatchResult> oversized = await service.RunAsync(
            ["pc-01", "pc-02", "pc-03"], ScanCredentials.CurrentUser, CancellationToken.None);

        Assert.Equal(ErrorCode.InvalidRequest, empty.Error!.Code);
        Assert.Equal(ErrorCode.InvalidRequest, oversized.Error!.Code);
        Assert.Contains("at most 2 hosts", oversized.Error.Message, StringComparison.Ordinal);
        Assert.Empty(_publishedEvents);
    }

    [Fact]
    public async Task OneFailedHost_DoesNotAbortSuccessfulInventoryCapture()
    {
        SetUpInventoryQueries("pc-down");

        Result<InventoryBatchResult> result = await CreateService().RunAsync(
            [" pc-down ", "pc-up", "PC-UP"],
            ScanCredentials.CurrentUser,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Hosts.Count);
        Assert.Equal(InventoryBatchHostStatus.Failed, result.Value.Hosts[0].Status);
        Assert.True(
            result.Value.Hosts[0].Error!.Code == ErrorCode.WmiUnavailable,
            $"Actual error: {result.Value.Hosts[0].Error}. Exception: {_batchLogger.LastException}");
        Assert.Equal(InventoryBatchHostStatus.Completed, result.Value.Hosts[1].Status);
        Assert.Equal("pc-up", result.Value.Hosts[1].Inventory!.Host);

        List<InventoryBatchProgress> progress;
        lock (_publishedEvents)
        {
            progress = _publishedEvents
                .Select(bridgeEvent => Assert.IsType<InventoryBatchProgress>(bridgeEvent.Payload))
                .ToList();
        }
        Assert.All(_publishedEvents, bridgeEvent =>
        {
            Assert.Equal("inventory", bridgeEvent.Module);
            Assert.Equal("batchScanProgress", bridgeEvent.EventName);
        });
        Assert.Contains(progress, item => item.Host == "pc-up" && item.Status == InventoryBatchHostStatus.Queued);
        Assert.Contains(progress, item => item.Host == "pc-up" && item.Status == InventoryBatchHostStatus.Running);
        Assert.Contains(progress, item => item.Host == "pc-up" && item.Status == InventoryBatchHostStatus.Completed);
    }

    private BatchInventoryService CreateService(int maxParallelScans = 2, int maxBatchHosts = 50)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        var inventoryOptions = MsOptions.Create(new InventoryOptions());
        var remoteOptions = MsOptions.Create(new RemoteScanOptions
        {
            MaxParallelScans = maxParallelScans,
            MaxBatchHosts = maxBatchHosts,
        });

        var services = new ServiceCollection();
        services.AddSingleton(_wmiQueryService);
        services.AddSingleton(_repository);
        services.AddSingleton(_registryReader);
        services.AddSingleton(clock);
        services.AddSingleton(inventoryOptions);
        services.AddSingleton(remoteOptions);
        services.AddSingleton<ILogger<HardwareInfoService>>(NullLogger<HardwareInfoService>.Instance);
        services.AddScoped<InstalledSoftwareReader>();
        services.AddScoped<RemoteInstalledSoftwareReader>();
        services.AddScoped<DeviceUserEvidenceCollector>();
        services.AddScoped<HardwareInfoService>();
        ServiceProvider provider = services.BuildServiceProvider();

        return new BatchInventoryService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            _eventPublisher,
            clock,
            remoteOptions,
            _batchLogger);
    }

    private void SetUpInventoryQueries(string failingHost)
    {
        var properties = new Dictionary<string, object?>
        {
            ["Name"] = "Test device",
            ["NumberOfCores"] = 4u,
            ["NumberOfLogicalProcessors"] = 8u,
            ["MaxClockSpeed"] = 3200u,
            ["Manufacturer"] = "Test",
            ["PartNumber"] = "TEST-1",
            ["Capacity"] = 8_589_934_592ul,
            ["Model"] = "Test disk",
            ["Size"] = 268_435_456_000ul,
            ["Caption"] = "Windows 11",
            ["Version"] = "10.0",
            ["BuildNumber"] = "26200",
            ["OSArchitecture"] = "64-bit",
            ["Index"] = 1u,
            ["NetEnabled"] = true,
            ["UserName"] = null,
        };
        _wmiQueryService.QueryAsync(
                Arg.Any<ScanTarget>(),
                Arg.Any<ScanCredentials>(),
                Arg.Any<ConnectionOptions>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                ScanTarget target = call.ArgAt<ScanTarget>(0);
                return string.Equals(target.Host, failingHost, StringComparison.OrdinalIgnoreCase)
                    ? Result.Failure<IReadOnlyList<WmiInstance>>(Error.WmiUnavailable("Host unavailable."))
                    : Result.Success<IReadOnlyList<WmiInstance>>([new WmiInstance(properties)]);
            });
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public Exception? LastException { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (exception is not null)
            {
                LastException = exception;
            }
        }
    }
}
