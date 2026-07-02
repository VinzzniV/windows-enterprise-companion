using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Inventory.Application;
using Wec.Modules.Inventory.Domain;
using Wec.Modules.Inventory.Persistence;

namespace Wec.Modules.Inventory.Tests.Application;

public class HardwareInfoServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);

    private readonly IWmiQueryService _wmiQueryService = Substitute.For<IWmiQueryService>();
    private readonly IHardwareSnapshotRepository _repository = Substitute.For<IHardwareSnapshotRepository>();
    private readonly IClock _clock = Substitute.For<IClock>();

    private HardwareInfoService CreateService(TimeSpan? cacheTtl = null)
    {
        _clock.UtcNow.Returns(Now);
        return new HardwareInfoService(
            _wmiQueryService,
            _repository,
            _clock,
            Microsoft.Extensions.Options.Options.Create(new InventoryOptions
            {
                CacheTtl = cacheTtl ?? TimeSpan.FromMinutes(15),
            }),
            NullLogger<HardwareInfoService>.Instance);
    }

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
    }

    private void SetUpQuery(string wmiClassName, Dictionary<string, object?> properties) =>
        _wmiQueryService
            .QueryAsync(
                Arg.Any<string>(),
                Arg.Is<string>(query => query.Contains(wmiClassName, StringComparison.Ordinal)),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<WmiInstance>>([new WmiInstance(properties)]));

    [Fact]
    public async Task FreshQuery_SavesSnapshotAndReturnsIt()
    {
        _repository.GetLatestAsync(Arg.Any<CancellationToken>()).Returns((CachedHardwareSnapshot?)null);
        SetUpSuccessfulWmiQueries();
        HardwareInfoService service = CreateService();

        Result<HardwareInfoResult> result = await service.GetHardwareInfoAsync(forceRefresh: false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.FromCache);
        Assert.Equal(Now, result.Value.CapturedAtUtc);
        Assert.Equal("Test CPU", result.Value.Snapshot.Cpu.Name);
        Assert.Equal(8, result.Value.Snapshot.Cpu.PhysicalCores);
        Assert.Equal(17179869184, Assert.Single(result.Value.Snapshot.MemoryBanks).CapacityBytes);
        Assert.Equal("Test SSD", Assert.Single(result.Value.Snapshot.Disks).Model);
        Assert.Equal("Microsoft Windows 11 Pro", result.Value.Snapshot.OperatingSystem.Caption);
        await _repository.Received(1).SaveAsync(result.Value.Snapshot, Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WmiFailure_PropagatesTypedErrorWithoutSaving()
    {
        _repository.GetLatestAsync(Arg.Any<CancellationToken>()).Returns((CachedHardwareSnapshot?)null);
        _wmiQueryService
            .QueryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<WmiInstance>>(Error.WmiUnavailable("WMI service unreachable")));
        HardwareInfoService service = CreateService();

        Result<HardwareInfoResult> result = await service.GetHardwareInfoAsync(forceRefresh: false, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.WmiUnavailable, result.Error!.Code);
        await _repository.DidNotReceive().SaveAsync(
            Arg.Any<HardwareSnapshot>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FreshCacheEntry_IsServedWithoutQueryingWmi()
    {
        HardwareSnapshot cachedSnapshot = BuildSnapshot();
        _repository.GetLatestAsync(Arg.Any<CancellationToken>())
            .Returns(new CachedHardwareSnapshot(cachedSnapshot, Now.AddMinutes(-5)));
        HardwareInfoService service = CreateService(cacheTtl: TimeSpan.FromMinutes(15));

        Result<HardwareInfoResult> result = await service.GetHardwareInfoAsync(forceRefresh: false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.FromCache);
        Assert.Same(cachedSnapshot, result.Value.Snapshot);
        await _wmiQueryService.DidNotReceive()
            .QueryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExpiredCacheEntry_TriggersFreshQuery()
    {
        _repository.GetLatestAsync(Arg.Any<CancellationToken>())
            .Returns(new CachedHardwareSnapshot(BuildSnapshot(), Now.AddMinutes(-30)));
        SetUpSuccessfulWmiQueries();
        HardwareInfoService service = CreateService(cacheTtl: TimeSpan.FromMinutes(15));

        Result<HardwareInfoResult> result = await service.GetHardwareInfoAsync(forceRefresh: false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.FromCache);
    }

    [Fact]
    public async Task ForceRefresh_BypassesCacheLookup()
    {
        SetUpSuccessfulWmiQueries();
        HardwareInfoService service = CreateService();

        Result<HardwareInfoResult> result = await service.GetHardwareInfoAsync(forceRefresh: true, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.FromCache);
        await _repository.DidNotReceive().GetLatestAsync(Arg.Any<CancellationToken>());
    }

    private static HardwareSnapshot BuildSnapshot() => new(
        new CpuInfo("Cached CPU", 4, 8, 3000),
        [new MemoryBank("Cached", "C-1", 8589934592, 2666)],
        [new DiskDrive("Cached disk", 256000000000, "SCSI", "Fixed")],
        new OperatingSystemInfo("Cached OS", "10.0", "22631", "64-bit"));
}
