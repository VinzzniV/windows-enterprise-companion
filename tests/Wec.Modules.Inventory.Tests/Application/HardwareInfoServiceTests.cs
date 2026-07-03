using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.Inventory.Application;
using Wec.Modules.Inventory.Domain;
using Wec.Modules.Inventory.Persistence;

namespace Wec.Modules.Inventory.Tests.Application;

public class HardwareInfoServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly string LocalHostKey = ScanTarget.Local.CacheKey;

    private readonly IWmiQueryService _wmiQueryService = Substitute.For<IWmiQueryService>();
    private readonly IHardwareSnapshotRepository _repository = Substitute.For<IHardwareSnapshotRepository>();
    private readonly IRegistryReader _registryReader = Substitute.For<IRegistryReader>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public HardwareInfoServiceTests()
    {
        _registryReader.ReadLocalMachineSubKeyNames(Arg.Any<string>())
            .Returns(Result.Success<IReadOnlyList<string>>([]));
    }

    private HardwareInfoService CreateService(TimeSpan? cacheTtl = null)
    {
        _clock.UtcNow.Returns(Now);
        return new HardwareInfoService(
            _wmiQueryService,
            _repository,
            new InstalledSoftwareReader(_registryReader),
            _clock,
            Microsoft.Extensions.Options.Options.Create(new InventoryOptions
            {
                CacheTtl = cacheTtl ?? TimeSpan.FromMinutes(15),
            }),
            Microsoft.Extensions.Options.Options.Create(new RemoteScanOptions()),
            NullLogger<HardwareInfoService>.Instance);
    }

    private static Task<Result<HardwareInfoResult>> GetLocalHardwareInfoAsync(
        HardwareInfoService service, bool forceRefresh) =>
        service.GetHardwareInfoAsync(
            ScanTarget.Local, ScanCredentials.CurrentUser, forceRefresh, CancellationToken.None);

    private void SetUpSuccessfulWmiQueries()
    {
        SetUpQuery("Win32_Processor", new Dictionary<string, object?>
        {
            ["Name"] = "Test CPU",
            ["NumberOfCores"] = 8u,
            ["NumberOfLogicalProcessors"] = 16u,
            ["MaxClockSpeed"] = 3600u,
        });
        SetUpQuery("Win32_PhysicalMemory", new Dictionary<string, object?>
        {
            ["Manufacturer"] = "TestRAM",
            ["PartNumber"] = "TR-123",
            ["Capacity"] = 17179869184ul,
            ["Speed"] = 3200u,
        });
        SetUpQuery("Win32_DiskDrive", new Dictionary<string, object?>
        {
            ["Model"] = "Test SSD",
            ["Size"] = 512110190592ul,
            ["InterfaceType"] = "SCSI",
            ["MediaType"] = "Fixed hard disk media",
        });
        SetUpQuery("Win32_OperatingSystem", new Dictionary<string, object?>
        {
            ["Caption"] = "Microsoft Windows 11 Pro",
            ["Version"] = "10.0.26200",
            ["BuildNumber"] = "26200",
            ["OSArchitecture"] = "64-bit",
        });
        SetUpQuery("Win32_NetworkAdapter", new Dictionary<string, object?>
        {
            ["Index"] = 1u,
            ["Name"] = "Test Ethernet",
            ["MACAddress"] = "00:11:22:33:44:55",
            ["Speed"] = 1000000000ul,
            ["NetEnabled"] = true,
            ["AdapterType"] = "Ethernet 802.3",
        });
        // Registered after the adapter setup: its matcher ("Win32_NetworkAdapter")
        // also matches the configuration query, and the last NSubstitute setup wins
        SetUpQuery("Win32_NetworkAdapterConfiguration", new Dictionary<string, object?>
        {
            ["Index"] = 1u,
            ["IPAddress"] = new[] { "192.168.1.10", "fe80::1" },
        });
        SetUpQuery("Win32_VideoController", new Dictionary<string, object?>
        {
            ["Name"] = "Test GPU",
            ["AdapterRAM"] = 8589934592u,
            ["DriverVersion"] = "31.0.1",
        });
        SetUpQuery("WmiMonitorID", new Dictionary<string, object?>
        {
            ["ManufacturerName"] = Encode("DEL"),
            ["UserFriendlyName"] = Encode("DELL U2723QE"),
            ["SerialNumberID"] = Encode("SN-1"),
        });
    }

    private static ushort[] Encode(string text) =>
        [.. text.Select(character => (ushort)character), 0];

    private void SetUpQuery(string wmiClassName, Dictionary<string, object?> properties) =>
        _wmiQueryService
            .QueryAsync(
                Arg.Any<ScanTarget>(),
                Arg.Any<ScanCredentials>(),
                Arg.Any<ConnectionOptions>(),
                Arg.Any<string>(),
                Arg.Is<string>(query => query.Contains(wmiClassName, StringComparison.Ordinal)),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<WmiInstance>>([new WmiInstance(properties)]));

    [Fact]
    public async Task FreshQuery_SavesSnapshotAndReturnsIt()
    {
        _repository.GetLatestAsync(LocalHostKey, Arg.Any<CancellationToken>())
            .Returns((CachedHardwareSnapshot?)null);
        SetUpSuccessfulWmiQueries();
        HardwareInfoService service = CreateService();

        Result<HardwareInfoResult> result = await GetLocalHardwareInfoAsync(service, forceRefresh: false);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.FromCache);
        Assert.Equal(Environment.MachineName, result.Value.Host);
        Assert.Equal(Now, result.Value.CapturedAtUtc);
        Assert.Equal("Test CPU", result.Value.Snapshot.Cpu.Name);
        Assert.Equal(8, result.Value.Snapshot.Cpu.PhysicalCores);
        Assert.Equal(17179869184, Assert.Single(result.Value.Snapshot.MemoryBanks).CapacityBytes);
        Assert.Equal("Test SSD", Assert.Single(result.Value.Snapshot.Disks).Model);
        Assert.Equal("Microsoft Windows 11 Pro", result.Value.Snapshot.OperatingSystem.Caption);
        PhysicalNetworkAdapter adapter = Assert.Single(result.Value.Snapshot.NetworkAdapters!);
        Assert.Equal("00:11:22:33:44:55", adapter.MacAddress);
        Assert.Equal(1000000000, adapter.SpeedBitsPerSecond);
        Assert.Equal(["192.168.1.10", "fe80::1"], adapter.IpAddresses);
        Assert.Equal("Test GPU", Assert.Single(result.Value.Snapshot.Gpus!).Name);
        MonitorInfo monitor = Assert.Single(result.Value.Snapshot.Monitors!);
        Assert.Equal("DEL", monitor.Manufacturer);
        Assert.Equal("DELL U2723QE", monitor.Model);
        Assert.NotNull(result.Value.Snapshot.InstalledSoftware);
        await _repository.Received(1).SaveAsync(
            LocalHostKey, result.Value.Snapshot, Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoteTarget_CachesUnderRemoteHostAndSkipsInstalledSoftware()
    {
        SetUpSuccessfulWmiQueries();
        ScanTarget remoteTarget = ScanTarget.Remote("pc-042.contoso.local");
        _repository.GetLatestAsync(remoteTarget.CacheKey, Arg.Any<CancellationToken>())
            .Returns((CachedHardwareSnapshot?)null);
        HardwareInfoService service = CreateService();

        Result<HardwareInfoResult> result = await service.GetHardwareInfoAsync(
            remoteTarget, ScanCredentials.CurrentUser, forceRefresh: false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("pc-042.contoso.local", result.Value.Host);
        Assert.Null(result.Value.Snapshot.InstalledSoftware);
        await _repository.Received(1).SaveAsync(
            remoteTarget.CacheKey, result.Value.Snapshot, Now, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(long.MaxValue)]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void NormalizeLinkSpeed_TreatsSentinelsAsUnknown(long reportedSpeed)
    {
        Assert.Null(HardwareInfoService.NormalizeLinkSpeed(reportedSpeed));
    }

    [Fact]
    public void NormalizeLinkSpeed_KeepsPlausibleValues()
    {
        Assert.Equal(2_500_000_000, HardwareInfoService.NormalizeLinkSpeed(2_500_000_000));
    }

    [Fact]
    public async Task Adapters_SortConnectedFirstAndDropSentinelSpeed()
    {
        _repository.GetLatestAsync(LocalHostKey, Arg.Any<CancellationToken>())
            .Returns((CachedHardwareSnapshot?)null);
        SetUpSuccessfulWmiQueries();
        _wmiQueryService
            .QueryAsync(
                Arg.Any<ScanTarget>(),
                Arg.Any<ScanCredentials>(),
                Arg.Any<ConnectionOptions>(),
                Arg.Any<string>(),
                Arg.Is<string>(query =>
                    query.Contains("Win32_NetworkAdapter", StringComparison.Ordinal)
                    && !query.Contains("Configuration", StringComparison.Ordinal)),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<WmiInstance>>(
            [
                new WmiInstance(new Dictionary<string, object?>
                {
                    ["Index"] = 2u,
                    ["Name"] = "Disconnected Wi-Fi",
                    ["Speed"] = unchecked((ulong)long.MaxValue),
                    ["NetEnabled"] = false,
                }),
                new WmiInstance(new Dictionary<string, object?>
                {
                    ["Index"] = 1u,
                    ["Name"] = "Test Ethernet",
                    ["Speed"] = 1000000000ul,
                    ["NetEnabled"] = true,
                }),
            ]));
        HardwareInfoService service = CreateService();

        Result<HardwareInfoResult> result = await GetLocalHardwareInfoAsync(service, forceRefresh: false);

        Assert.True(result.IsSuccess);
        IReadOnlyList<PhysicalNetworkAdapter> adapters = result.Value.Snapshot.NetworkAdapters!;
        Assert.Equal(2, adapters.Count);
        Assert.Equal("Test Ethernet", adapters[0].Name);
        Assert.True(adapters[0].Connected);
        Assert.Equal("Disconnected Wi-Fi", adapters[1].Name);
        Assert.Null(adapters[1].SpeedBitsPerSecond);
    }

    [Fact]
    public async Task MonitorQueryFailure_YieldsEmptyMonitorsInsteadOfFailing()
    {
        _repository.GetLatestAsync(LocalHostKey, Arg.Any<CancellationToken>())
            .Returns((CachedHardwareSnapshot?)null);
        SetUpSuccessfulWmiQueries();
        _wmiQueryService
            .QueryAsync(
                Arg.Any<ScanTarget>(),
                Arg.Any<ScanCredentials>(),
                Arg.Any<ConnectionOptions>(),
                Arg.Any<string>(),
                Arg.Is<string>(query => query.Contains("WmiMonitorID", StringComparison.Ordinal)),
                Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<WmiInstance>>(Error.WmiUnavailable("no monitor class")));
        HardwareInfoService service = CreateService();

        Result<HardwareInfoResult> result = await GetLocalHardwareInfoAsync(service, forceRefresh: false);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Snapshot.Monitors!);
    }

    [Fact]
    public async Task WmiFailure_PropagatesTypedErrorWithoutSaving()
    {
        _repository.GetLatestAsync(LocalHostKey, Arg.Any<CancellationToken>())
            .Returns((CachedHardwareSnapshot?)null);
        _wmiQueryService
            .QueryAsync(
                Arg.Any<ScanTarget>(),
                Arg.Any<ScanCredentials>(),
                Arg.Any<ConnectionOptions>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<WmiInstance>>(Error.WmiUnavailable("WMI service unreachable")));
        HardwareInfoService service = CreateService();

        Result<HardwareInfoResult> result = await GetLocalHardwareInfoAsync(service, forceRefresh: false);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.WmiUnavailable, result.Error!.Code);
        await _repository.DidNotReceive().SaveAsync(
            Arg.Any<string>(), Arg.Any<HardwareSnapshot>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FreshCacheEntry_IsServedWithoutQueryingWmi()
    {
        HardwareSnapshot cachedSnapshot = BuildSnapshot();
        _repository.GetLatestAsync(LocalHostKey, Arg.Any<CancellationToken>())
            .Returns(new CachedHardwareSnapshot(cachedSnapshot, Now.AddMinutes(-5)));
        HardwareInfoService service = CreateService(cacheTtl: TimeSpan.FromMinutes(15));

        Result<HardwareInfoResult> result = await GetLocalHardwareInfoAsync(service, forceRefresh: false);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.FromCache);
        Assert.Same(cachedSnapshot, result.Value.Snapshot);
        await _wmiQueryService.DidNotReceive().QueryAsync(
            Arg.Any<ScanTarget>(),
            Arg.Any<ScanCredentials>(),
            Arg.Any<ConnectionOptions>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExpiredCacheEntry_TriggersFreshQuery()
    {
        _repository.GetLatestAsync(LocalHostKey, Arg.Any<CancellationToken>())
            .Returns(new CachedHardwareSnapshot(BuildSnapshot(), Now.AddMinutes(-30)));
        SetUpSuccessfulWmiQueries();
        HardwareInfoService service = CreateService(cacheTtl: TimeSpan.FromMinutes(15));

        Result<HardwareInfoResult> result = await GetLocalHardwareInfoAsync(service, forceRefresh: false);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.FromCache);
    }

    [Fact]
    public async Task ForceRefresh_BypassesCacheLookup()
    {
        SetUpSuccessfulWmiQueries();
        HardwareInfoService service = CreateService();

        Result<HardwareInfoResult> result = await GetLocalHardwareInfoAsync(service, forceRefresh: true);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.FromCache);
        await _repository.DidNotReceive().GetLatestAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static HardwareSnapshot BuildSnapshot() => new(
        new CpuInfo("Cached CPU", 4, 8, 3000),
        [new MemoryBank("Cached", "C-1", 8589934592, 2666)],
        [new DiskDrive("Cached disk", 256000000000, "SCSI", "Fixed")],
        new OperatingSystemInfo("Cached OS", "10.0", "22631", "64-bit"));
}
