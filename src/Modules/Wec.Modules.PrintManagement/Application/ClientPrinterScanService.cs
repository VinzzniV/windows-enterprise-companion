using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.PrintManagement.Domain;

namespace Wec.Modules.PrintManagement.Application;

/// <summary>
/// Captures the printers installed on a client (local or network connections)
/// over CIM (root\StandardCimv2, local or remote via the existing WMI seam).
/// This is the deliberate counterpart to the print-server scan: printers a
/// client uses, without SNMP device enrichment.
/// </summary>
public sealed class ClientPrinterScanService
{
    private const string StandardCimV2Namespace = @"root\StandardCimv2";

    private readonly IWmiQueryService _wmiQueryService;
    private readonly IClock _clock;
    private readonly RemoteScanOptions _remoteOptions;

    public ClientPrinterScanService(
        IWmiQueryService wmiQueryService, IClock clock, IOptions<RemoteScanOptions> remoteOptions)
    {
        _wmiQueryService = wmiQueryService;
        _clock = clock;
        _remoteOptions = remoteOptions.Value;
    }

    public async Task<Result<ClientPrinterScan>> CaptureAsync(
        ScanTarget target, ScanCredentials credentials, CancellationToken cancellationToken)
    {
        ConnectionOptions connection = _remoteOptions.ToConnectionOptions();

        Result<IReadOnlyList<WmiInstance>> printers = await _wmiQueryService.QueryAsync(
            target, credentials, connection, StandardCimV2Namespace,
            "SELECT Name, ShareName, DriverName, PortName, Location, Shared FROM MSFT_Printer",
            cancellationToken);
        if (printers.IsFailure)
        {
            return Result.Failure<ClientPrinterScan>(printers.Error!);
        }

        List<ClientPrinter> mapped = [.. printers.Value
            .Select(MapPrinter)
            .OfType<ClientPrinter>()
            .OrderBy(printer => printer.Name, StringComparer.OrdinalIgnoreCase)];

        return Result.Success(new ClientPrinterScan(target.CacheKey, _clock.UtcNow, mapped));
    }

    internal static ClientPrinter? MapPrinter(WmiInstance instance)
    {
        if (instance.GetString("Name") is not { Length: > 0 } name)
        {
            return null;
        }

        return new ClientPrinter(
            name,
            NonEmpty(instance.GetString("DriverName")),
            NonEmpty(instance.GetString("PortName")),
            NonEmpty(instance.GetString("Location")),
            AsBool(instance, "Shared"),
            IsNetworkName(name));
    }

    /// <summary>A network connection is named \\server\queue; a local printer is not.</summary>
    internal static bool IsNetworkName(string name) => name.StartsWith(@"\\", StringComparison.Ordinal);

    private static bool AsBool(WmiInstance instance, string property) => instance.GetRawValue(property) switch
    {
        bool value => value,
        string text => bool.TryParse(text, out bool parsed) && parsed,
        _ => false,
    };

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
