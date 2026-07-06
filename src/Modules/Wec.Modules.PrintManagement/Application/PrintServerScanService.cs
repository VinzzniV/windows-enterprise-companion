using System.Text.Json;
using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Core.Snmp;
using Wec.Core.Targets;
using Wec.Modules.PrintManagement.Domain;

namespace Wec.Modules.PrintManagement.Application;

/// <summary>
/// Captures one print server: queues/drivers/ports over CIM
/// (root\StandardCimv2, local or remote via the existing WMI seam), then
/// enriches every distinct port address over SNMP. Devices that do not
/// answer become per-printer errors, never a scan abort (ADR 0009).
/// </summary>
public sealed class PrintServerScanService
{
    private const string StandardCimV2Namespace = @"root\StandardCimv2";

    private const string SerialNumberOid = "1.3.6.1.2.1.43.5.1.1.17.1";
    private const string ModelOid = "1.3.6.1.2.1.25.3.2.1.3.1";
    private const string SysNameOid = "1.3.6.1.2.1.1.5.0";
    private const string SysLocationOid = "1.3.6.1.2.1.1.6.0";
    private const string PrinterStatusOid = "1.3.6.1.2.1.25.3.5.1.1.1";
    private const string PageCountOid = "1.3.6.1.2.1.43.10.2.1.4.1.1";
    private const string SuppliesTableOid = "1.3.6.1.2.1.43.11.1.1";

    private readonly IWmiQueryService _wmiQueryService;
    private readonly ISnmpReader _snmpReader;
    private readonly IClock _clock;
    private readonly PrintManagementOptions _options;
    private readonly RemoteScanOptions _remoteOptions;

    public PrintServerScanService(
        IWmiQueryService wmiQueryService,
        ISnmpReader snmpReader,
        IClock clock,
        IOptions<PrintManagementOptions> options,
        IOptions<RemoteScanOptions> remoteOptions)
    {
        _wmiQueryService = wmiQueryService;
        _snmpReader = snmpReader;
        _clock = clock;
        _options = options.Value;
        _remoteOptions = remoteOptions.Value;
    }

    public async Task<Result<PrintServerSnapshot>> CaptureAsync(
        ScanTarget target, ScanCredentials credentials, CancellationToken cancellationToken)
    {
        ConnectionOptions connection = _remoteOptions.ToConnectionOptions();

        Result<IReadOnlyList<WmiInstance>> printers = await _wmiQueryService.QueryAsync(
            target, credentials, connection, StandardCimV2Namespace,
            "SELECT Name, ShareName, DriverName, PortName, Location, Comment FROM MSFT_Printer",
            cancellationToken);
        if (printers.IsFailure)
        {
            return Result.Failure<PrintServerSnapshot>(printers.Error!);
        }

        Result<IReadOnlyList<WmiInstance>> ports = await _wmiQueryService.QueryAsync(
            target, credentials, connection, StandardCimV2Namespace,
            "SELECT * FROM MSFT_PrinterPort",
            cancellationToken);
        if (ports.IsFailure)
        {
            return Result.Failure<PrintServerSnapshot>(ports.Error!);
        }

        Result<IReadOnlyList<WmiInstance>> drivers = await _wmiQueryService.QueryAsync(
            target, credentials, connection, StandardCimV2Namespace,
            "SELECT Name, DriverVersion FROM MSFT_PrinterDriver",
            cancellationToken);
        if (drivers.IsFailure)
        {
            return Result.Failure<PrintServerSnapshot>(drivers.Error!);
        }

        var addressByPort = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (WmiInstance port in ports.Value)
        {
            // PrinterHostAddress only exists on TCP/IP ports — WSD/local ports stay CIM-only
            if (port.GetString("Name") is { Length: > 0 } name
                && port.GetString("PrinterHostAddress") is { Length: > 0 } address)
            {
                addressByPort[name] = address;
            }
        }

        var driverVersionByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (WmiInstance driver in drivers.Value)
        {
            if (driver.GetString("Name") is { Length: > 0 } name
                && FormatDriverVersion(driver.GetInteger("DriverVersion")) is { } version)
            {
                driverVersionByName[name] = version;
            }
        }

        var queueRows = new List<(string Queue, string? Share, string? Driver, string? Port, string? Location, string? Comment, string? Address)>();
        foreach (WmiInstance printer in printers.Value)
        {
            if (printer.GetString("Name") is not { Length: > 0 } queueName)
            {
                continue;
            }

            string? portName = printer.GetString("PortName");
            queueRows.Add((
                queueName,
                printer.GetString("ShareName"),
                printer.GetString("DriverName"),
                portName,
                printer.GetString("Location"),
                printer.GetString("Comment"),
                portName is not null ? addressByPort.GetValueOrDefault(portName) : null));
        }

        IReadOnlyDictionary<string, Result<PrinterDevice>> devices = await QueryDevicesAsync(
            [.. queueRows.Select(row => row.Address).Where(address => address is not null).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase)],
            cancellationToken);

        List<PrinterEntry> entries = [.. queueRows
            .Select(row =>
            {
                Result<PrinterDevice>? device = row.Address is not null
                    ? devices.GetValueOrDefault(row.Address)
                    : null;
                return new PrinterEntry(
                    row.Queue,
                    row.Share,
                    row.Driver,
                    row.Driver is not null ? driverVersionByName.GetValueOrDefault(row.Driver) : null,
                    row.Port,
                    row.Address,
                    row.Location,
                    row.Comment,
                    device is { IsSuccess: true } ? device.Value : null,
                    device is { IsFailure: true }
                        ? new DeviceQueryError(
                            JsonNamingPolicy.SnakeCaseUpper.ConvertName(device.Error!.Code.ToString()),
                            device.Error.Message)
                        : null);
            })
            .OrderBy(entry => entry.QueueName, StringComparer.OrdinalIgnoreCase)];

        return Result.Success(new PrintServerSnapshot(target.CacheKey, _clock.UtcNow, entries));
    }

    private async Task<IReadOnlyDictionary<string, Result<PrinterDevice>>> QueryDevicesAsync(
        IReadOnlyList<string> addresses, CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, Result<PrinterDevice>>(StringComparer.OrdinalIgnoreCase);
        using var throttle = new SemaphoreSlim(Math.Max(1, _remoteOptions.MaxParallelScans));
        await Task.WhenAll(addresses.Select(async address =>
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                Result<PrinterDevice> device = await QueryDeviceAsync(address, cancellationToken);
                lock (results)
                {
                    results[address] = device;
                }
            }
            finally
            {
                throttle.Release();
            }
        }));
        return results;
    }

    private async Task<Result<PrinterDevice>> QueryDeviceAsync(
        string address, CancellationToken cancellationToken)
    {
        var endpoint = new SnmpEndpoint(address, _options.SnmpPort, _options.SnmpCommunity, _options.SnmpTimeout);

        Result<IReadOnlyList<SnmpVarBind>> values = await _snmpReader.GetAsync(
            endpoint,
            [SerialNumberOid, ModelOid, SysNameOid, SysLocationOid, PrinterStatusOid, PageCountOid],
            cancellationToken);
        if (values.IsFailure)
        {
            return Result.Failure<PrinterDevice>(values.Error!);
        }

        Result<IReadOnlyList<SnmpVarBind>> supplies = await _snmpReader.WalkAsync(
            endpoint, SuppliesTableOid, cancellationToken);
        if (supplies.IsFailure)
        {
            return Result.Failure<PrinterDevice>(supplies.Error!);
        }

        return Result.Success(new PrinterDevice(
            NonEmpty(values.Value[0].Value.Text),
            NonEmpty(values.Value[1].Value.Text),
            NonEmpty(values.Value[2].Value.Text),
            NonEmpty(values.Value[3].Value.Text),
            MapPrinterStatus(values.Value[4].Value.Number),
            values.Value[5].Value.Number,
            ParseSupplies(supplies.Value, _options.LowTonerThresholdPercent)));
    }

    internal static string? MapPrinterStatus(long? statusCode) => statusCode switch
    {
        1 => "Other",
        2 => "Unknown",
        3 => "Idle",
        4 => "Printing",
        5 => "Warmup",
        _ => null,
    };

    /// <summary>MSFT_PrinterDriver.DriverVersion packs four UInt16 parts into a UInt64.</summary>
    internal static string? FormatDriverVersion(long? packedVersion)
    {
        if (packedVersion is not { } packed)
        {
            return null;
        }

        ulong value = unchecked((ulong)packed);
        return $"{(value >> 48) & 0xFFFF}.{(value >> 32) & 0xFFFF}.{(value >> 16) & 0xFFFF}.{value & 0xFFFF}";
    }

    /// <summary>
    /// Parses a prtMarkerSuppliesEntry walk (columns 6 = description,
    /// 8 = max capacity, 9 = level; OID suffix "column.device.index").
    /// RFC 3805: level -1/-2 = unknown, -3 = "some remaining" — those render
    /// as unknown percent instead of a fake number.
    /// </summary>
    internal static IReadOnlyList<TonerSupply> ParseSupplies(
        IReadOnlyList<SnmpVarBind> walkedSupplies, int lowThresholdPercent)
    {
        var descriptions = new Dictionary<string, string>(StringComparer.Ordinal);
        var maxCapacities = new Dictionary<string, long>(StringComparer.Ordinal);
        var levels = new Dictionary<string, long>(StringComparer.Ordinal);

        foreach (SnmpVarBind varBind in walkedSupplies)
        {
            string suffix = varBind.Oid.Length > SuppliesTableOid.Length + 1
                ? varBind.Oid[(SuppliesTableOid.Length + 1)..]
                : string.Empty;
            int firstDot = suffix.IndexOf('.', StringComparison.Ordinal);
            if (firstDot <= 0)
            {
                continue;
            }

            string column = suffix[..firstDot];
            string rowKey = suffix[(firstDot + 1)..];
            switch (column)
            {
                case "6" when varBind.Value.Text is { Length: > 0 } description:
                    descriptions[rowKey] = description;
                    break;
                case "8" when varBind.Value.Number is { } maxCapacity:
                    maxCapacities[rowKey] = maxCapacity;
                    break;
                case "9" when varBind.Value.Number is { } level:
                    levels[rowKey] = level;
                    break;
                default:
                    break;
            }
        }

        return [.. levels.Keys
            .Union(descriptions.Keys, StringComparer.Ordinal)
            .OrderBy(rowKey => rowKey, StringComparer.Ordinal)
            .Select(rowKey =>
            {
                long? level = levels.TryGetValue(rowKey, out long levelValue) ? levelValue : null;
                long? maxCapacity = maxCapacities.TryGetValue(rowKey, out long maxValue) ? maxValue : null;
                int? percent = level is >= 0 && maxCapacity is > 0
                    ? (int)Math.Round(100.0 * level.Value / maxCapacity.Value)
                    : null;
                return new TonerSupply(
                    descriptions.GetValueOrDefault(rowKey, $"Supply {rowKey}"),
                    percent,
                    percent is { } value && value <= lowThresholdPercent);
            })];
    }

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
