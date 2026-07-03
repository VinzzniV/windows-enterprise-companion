using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.Inventory.Domain;
using Wec.Modules.Inventory.Persistence;

namespace Wec.Modules.Inventory.Application;

public sealed partial class HardwareInfoService
{
    private const string CimV2Namespace = @"root\cimv2";
    private const string WmiRootNamespace = @"root\wmi";

    private readonly IWmiQueryService _wmiQueryService;
    private readonly IHardwareSnapshotRepository _repository;
    private readonly InstalledSoftwareReader _installedSoftwareReader;
    private readonly IClock _clock;
    private readonly InventoryOptions _options;
    private readonly ConnectionOptions _connectionOptions;
    private readonly ILogger<HardwareInfoService> _logger;

    public HardwareInfoService(
        IWmiQueryService wmiQueryService,
        IHardwareSnapshotRepository repository,
        InstalledSoftwareReader installedSoftwareReader,
        IClock clock,
        IOptions<InventoryOptions> options,
        IOptions<RemoteScanOptions> remoteScanOptions,
        ILogger<HardwareInfoService> logger)
    {
        _wmiQueryService = wmiQueryService;
        _repository = repository;
        _installedSoftwareReader = installedSoftwareReader;
        _clock = clock;
        _options = options.Value;
        _connectionOptions = remoteScanOptions.Value.ToConnectionOptions();
        _logger = logger;
    }

    public async Task<Result<HardwareInfoResult>> GetHardwareInfoAsync(
        ScanTarget target,
        ScanCredentials credentials,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        string hostKey = target.CacheKey;
        if (!forceRefresh)
        {
            CachedHardwareSnapshot? cached = await _repository.GetLatestAsync(hostKey, cancellationToken);
            if (cached is not null && _clock.UtcNow - cached.CapturedAtUtc < _options.CacheTtl)
            {
                LogServedFromCache(hostKey, cached.CapturedAtUtc);
                return Result.Success(new HardwareInfoResult(
                    target.DisplayName, cached.Snapshot, cached.CapturedAtUtc, FromCache: true));
            }
        }

        Result<HardwareSnapshot> snapshotResult = await QuerySnapshotAsync(target, credentials, cancellationToken);
        if (snapshotResult.IsFailure)
        {
            return Result.Failure<HardwareInfoResult>(snapshotResult.Error!);
        }

        DateTimeOffset capturedAtUtc = _clock.UtcNow;
        await _repository.SaveAsync(hostKey, snapshotResult.Value, capturedAtUtc, cancellationToken);
        LogCapturedFresh(hostKey, capturedAtUtc);
        return Result.Success(new HardwareInfoResult(
            target.DisplayName, snapshotResult.Value, capturedAtUtc, FromCache: false));
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Serving hardware snapshot for {HostKey} from cache, captured {CapturedAtUtc}")]
    private partial void LogServedFromCache(string hostKey, DateTimeOffset capturedAtUtc);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Captured fresh hardware snapshot for {HostKey} at {CapturedAtUtc}")]
    private partial void LogCapturedFresh(string hostKey, DateTimeOffset capturedAtUtc);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Monitor identification unavailable on {HostKey}: {ErrorMessage}")]
    private partial void LogMonitorsUnavailable(string hostKey, string errorMessage);

    private Task<Result<IReadOnlyList<WmiInstance>>> QueryAsync(
        ScanTarget target,
        ScanCredentials credentials,
        string wmiNamespace,
        string wqlQuery,
        CancellationToken cancellationToken) =>
        _wmiQueryService.QueryAsync(target, credentials, _connectionOptions, wmiNamespace, wqlQuery, cancellationToken);

    private async Task<Result<HardwareSnapshot>> QuerySnapshotAsync(
        ScanTarget target,
        ScanCredentials credentials,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<WmiInstance>> processors = await QueryAsync(
            target, credentials, CimV2Namespace,
            "SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor",
            cancellationToken);
        if (processors.IsFailure)
        {
            return Result.Failure<HardwareSnapshot>(processors.Error!);
        }

        Result<IReadOnlyList<WmiInstance>> memoryBanks = await QueryAsync(
            target, credentials, CimV2Namespace,
            "SELECT Manufacturer, PartNumber, Capacity, Speed FROM Win32_PhysicalMemory",
            cancellationToken);
        if (memoryBanks.IsFailure)
        {
            return Result.Failure<HardwareSnapshot>(memoryBanks.Error!);
        }

        Result<IReadOnlyList<WmiInstance>> disks = await QueryAsync(
            target, credentials, CimV2Namespace,
            "SELECT Model, Size, InterfaceType, MediaType FROM Win32_DiskDrive",
            cancellationToken);
        if (disks.IsFailure)
        {
            return Result.Failure<HardwareSnapshot>(disks.Error!);
        }

        Result<IReadOnlyList<WmiInstance>> operatingSystems = await QueryAsync(
            target, credentials, CimV2Namespace,
            "SELECT Caption, Version, BuildNumber, OSArchitecture FROM Win32_OperatingSystem",
            cancellationToken);
        if (operatingSystems.IsFailure)
        {
            return Result.Failure<HardwareSnapshot>(operatingSystems.Error!);
        }

        Result<IReadOnlyList<WmiInstance>> networkAdapters = await QueryAsync(
            target, credentials, CimV2Namespace,
            "SELECT Index, Name, MACAddress, Speed, NetEnabled, AdapterType FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE",
            cancellationToken);
        if (networkAdapters.IsFailure)
        {
            return Result.Failure<HardwareSnapshot>(networkAdapters.Error!);
        }

        // IP addresses are enrichment: a failing config query degrades to
        // adapters without addresses instead of failing the snapshot
        Result<IReadOnlyList<WmiInstance>> adapterConfigurations = await QueryAsync(
            target, credentials, CimV2Namespace,
            "SELECT Index, IPAddress FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE",
            cancellationToken);
        IReadOnlyDictionary<long, IReadOnlyList<string>> ipAddressesByAdapterIndex =
            adapterConfigurations.IsSuccess
                ? MapIpAddressesByAdapterIndex(adapterConfigurations.Value)
                : new Dictionary<long, IReadOnlyList<string>>();

        Result<IReadOnlyList<WmiInstance>> videoControllers = await QueryAsync(
            target, credentials, CimV2Namespace,
            "SELECT Name, AdapterRAM, DriverVersion FROM Win32_VideoController",
            cancellationToken);
        if (videoControllers.IsFailure)
        {
            return Result.Failure<HardwareSnapshot>(videoControllers.Error!);
        }

        // WmiMonitorID is absent on headless machines and many VMs — an empty
        // monitor list is a valid capture there, not a failed snapshot
        Result<IReadOnlyList<WmiInstance>> monitors = await QueryAsync(
            target, credentials, WmiRootNamespace,
            "SELECT ManufacturerName, UserFriendlyName, SerialNumberID FROM WmiMonitorID",
            cancellationToken);
        IReadOnlyList<MonitorInfo> monitorInfos;
        if (monitors.IsSuccess)
        {
            monitorInfos = monitors.Value.Select(ToMonitorInfo).ToList();
        }
        else
        {
            LogMonitorsUnavailable(target.CacheKey, monitors.Error!.Message);
            monitorInfos = [];
        }

        IReadOnlyList<InstalledSoftwareEntry>? installedSoftware = target.IsLocal
            ? _installedSoftwareReader.ReadInstalledSoftware()
            : null;

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
            ToOperatingSystemInfo(operatingSystem),
            networkAdapters.Value
                .Select(instance => ToNetworkAdapterInfo(instance, ipAddressesByAdapterIndex))
                .OrderByDescending(adapter => adapter.Connected == true)
                .ThenBy(adapter => adapter.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            videoControllers.Value.Select(ToGpuInfo).ToList(),
            monitorInfos,
            installedSoftware));
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

    // Win32_NetworkAdapter reports Int64.MaxValue (and similar sentinels) as
    // Speed for adapters whose link speed is unknown/disconnected
    private const long MaxPlausibleLinkSpeedBitsPerSecond = 1_000_000_000_000;

    internal static long? NormalizeLinkSpeed(long? reportedSpeed) =>
        reportedSpeed is > 0 and < MaxPlausibleLinkSpeedBitsPerSecond ? reportedSpeed : null;

    private static Dictionary<long, IReadOnlyList<string>> MapIpAddressesByAdapterIndex(
        IReadOnlyList<WmiInstance> configurations)
    {
        var byIndex = new Dictionary<long, IReadOnlyList<string>>();
        foreach (WmiInstance configuration in configurations)
        {
            long? adapterIndex = configuration.GetInteger("Index");
            if (adapterIndex is null || configuration.GetRawValue("IPAddress") is not string[] addresses)
            {
                continue;
            }

            byIndex[adapterIndex.Value] = addresses
                .Where(address => !string.IsNullOrWhiteSpace(address))
                .ToList();
        }

        return byIndex;
    }

    private static PhysicalNetworkAdapter ToNetworkAdapterInfo(
        WmiInstance instance,
        IReadOnlyDictionary<long, IReadOnlyList<string>> ipAddressesByAdapterIndex) => new(
        instance.GetString("Name") ?? "Unknown adapter",
        instance.GetString("MACAddress"),
        NormalizeLinkSpeed(instance.GetInteger("Speed")),
        instance.GetRawValue("NetEnabled") as bool?,
        instance.GetString("AdapterType"),
        instance.GetInteger("Index") is long adapterIndex
            && ipAddressesByAdapterIndex.TryGetValue(adapterIndex, out IReadOnlyList<string>? addresses)
            ? addresses
            : null);

    private static GpuInfo ToGpuInfo(WmiInstance instance) => new(
        instance.GetString("Name") ?? "Unknown GPU",
        instance.GetInteger("AdapterRAM"),
        instance.GetString("DriverVersion"));

    private static MonitorInfo ToMonitorInfo(WmiInstance instance) => new(
        DecodeMonitorText(instance.GetRawValue("ManufacturerName")),
        DecodeMonitorText(instance.GetRawValue("UserFriendlyName")),
        DecodeMonitorText(instance.GetRawValue("SerialNumberID")));

    private static string? DecodeMonitorText(object? rawValue)
    {
        if (rawValue is not ushort[] encodedCharacters)
        {
            return null;
        }

        string decoded = new(encodedCharacters
            .TakeWhile(character => character != 0)
            .Select(character => (char)character)
            .ToArray());
        return string.IsNullOrWhiteSpace(decoded) ? null : decoded.Trim();
    }
}
