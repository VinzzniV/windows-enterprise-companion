using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Inventory.Domain;
using Wec.Modules.Inventory.Persistence;

namespace Wec.Modules.Inventory.Application;

public sealed partial class HardwareInfoService
{
    private const string CimV2Namespace = @"root\cimv2";

    private readonly IWmiQueryService _wmiQueryService;
    private readonly IHardwareSnapshotRepository _repository;
    private readonly IClock _clock;
    private readonly InventoryOptions _options;
    private readonly ILogger<HardwareInfoService> _logger;

    public HardwareInfoService(
        IWmiQueryService wmiQueryService,
        IHardwareSnapshotRepository repository,
        IClock clock,
        IOptions<InventoryOptions> options,
        ILogger<HardwareInfoService> logger)
    {
        _wmiQueryService = wmiQueryService;
        _repository = repository;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result<HardwareInfoResult>> GetHardwareInfoAsync(
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (!forceRefresh)
        {
            CachedHardwareSnapshot? cached = await _repository.GetLatestAsync(cancellationToken);
            if (cached is not null && _clock.UtcNow - cached.CapturedAtUtc < _options.CacheTtl)
            {
                LogServedFromCache(cached.CapturedAtUtc);
                return Result.Success(new HardwareInfoResult(cached.Snapshot, cached.CapturedAtUtc, FromCache: true));
            }
        }

        Result<HardwareSnapshot> snapshotResult = await QuerySnapshotAsync(cancellationToken);
        if (snapshotResult.IsFailure)
        {
            return Result.Failure<HardwareInfoResult>(snapshotResult.Error!);
        }

        DateTimeOffset capturedAtUtc = _clock.UtcNow;
        await _repository.SaveAsync(snapshotResult.Value, capturedAtUtc, cancellationToken);
        LogCapturedFresh(capturedAtUtc);
        return Result.Success(new HardwareInfoResult(snapshotResult.Value, capturedAtUtc, FromCache: false));
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Serving hardware snapshot from cache, captured {CapturedAtUtc}")]
    private partial void LogServedFromCache(DateTimeOffset capturedAtUtc);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Captured fresh hardware snapshot at {CapturedAtUtc}")]
    private partial void LogCapturedFresh(DateTimeOffset capturedAtUtc);

    private async Task<Result<HardwareSnapshot>> QuerySnapshotAsync(CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<WmiInstance>> processors = await _wmiQueryService.QueryAsync(
            CimV2Namespace,
            "SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor",
            cancellationToken);
        if (processors.IsFailure)
        {
            return Result.Failure<HardwareSnapshot>(processors.Error!);
        }

        Result<IReadOnlyList<WmiInstance>> memoryBanks = await _wmiQueryService.QueryAsync(
            CimV2Namespace,
            "SELECT Manufacturer, PartNumber, Capacity, Speed FROM Win32_PhysicalMemory",
            cancellationToken);
        if (memoryBanks.IsFailure)
        {
            return Result.Failure<HardwareSnapshot>(memoryBanks.Error!);
        }

        Result<IReadOnlyList<WmiInstance>> disks = await _wmiQueryService.QueryAsync(
            CimV2Namespace,
            "SELECT Model, Size, InterfaceType, MediaType FROM Win32_DiskDrive",
            cancellationToken);
        if (disks.IsFailure)
        {
            return Result.Failure<HardwareSnapshot>(disks.Error!);
        }

        Result<IReadOnlyList<WmiInstance>> operatingSystems = await _wmiQueryService.QueryAsync(
            CimV2Namespace,
            "SELECT Caption, Version, BuildNumber, OSArchitecture FROM Win32_OperatingSystem",
            cancellationToken);
        if (operatingSystems.IsFailure)
        {
            return Result.Failure<HardwareSnapshot>(operatingSystems.Error!);
        }

        WmiInstance? processor = processors.Value.Count > 0 ? processors.Value[0] : null;
        WmiInstance? operatingSystem = operatingSystems.Value.Count > 0 ? operatingSystems.Value[0] : null;
        if (processor is null || operatingSystem is null)
        {
            return Result.Failure<HardwareSnapshot>(Error.WmiUnavailable(
                "WMI returned no processor or operating system instance."));
        }

        return Result.Success(new HardwareSnapshot(
            ToCpuInfo(processor),
            memoryBanks.Value.Select(ToMemoryBank).ToList(),
            disks.Value.Select(ToDiskDrive).ToList(),
            ToOperatingSystemInfo(operatingSystem)));
    }

    private static CpuInfo ToCpuInfo(WmiInstance instance) => new(
        instance.GetString("Name") ?? "Unknown CPU",
        (int)(instance.GetInteger("NumberOfCores") ?? 0),
        (int)(instance.GetInteger("NumberOfLogicalProcessors") ?? 0),
        (int)(instance.GetInteger("MaxClockSpeed") ?? 0));

    private static MemoryBank ToMemoryBank(WmiInstance instance) => new(
        instance.GetString("Manufacturer")?.Trim(),
        instance.GetString("PartNumber")?.Trim(),
        instance.GetInteger("Capacity") ?? 0,
        (int?)instance.GetInteger("Speed"));

    private static DiskDrive ToDiskDrive(WmiInstance instance) => new(
        instance.GetString("Model") ?? "Unknown disk",
        instance.GetInteger("Size") ?? 0,
        instance.GetString("InterfaceType"),
        instance.GetString("MediaType"));

    private static OperatingSystemInfo ToOperatingSystemInfo(WmiInstance instance) => new(
        instance.GetString("Caption") ?? "Unknown OS",
        instance.GetString("Version") ?? string.Empty,
        instance.GetString("BuildNumber") ?? string.Empty,
        instance.GetString("OSArchitecture"));
}
