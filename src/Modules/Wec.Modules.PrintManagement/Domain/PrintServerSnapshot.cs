namespace Wec.Modules.PrintManagement.Domain;

/// <summary>
/// One marker supply (RFC 3805). <see cref="Percent"/> is null when the
/// device reports the level as unknown (-2) or only as "some remaining" (-3);
/// <see cref="IsLow"/> is evaluated against the configured threshold at
/// capture time.
/// </summary>
public sealed record TonerSupply(string Description, int? Percent, bool IsLow);

public sealed record PrinterDevice(
    string? SerialNumber,
    string? Model,
    string? SysName,
    string? SysLocation,
    string? Status,
    long? PageCount,
    IReadOnlyList<TonerSupply> Supplies);

/// <summary>Mirrors the inventory SoftwareCaptureError shape: attempted and failed, visibly.</summary>
public sealed record DeviceQueryError(string Code, string Message);

/// <summary>
/// One queue on a print server, enriched with SNMP device data when the
/// port has an address and the device answered. CIM data stays visible when
/// the device does not (ADR 0009).
/// </summary>
public sealed record PrinterEntry(
    string QueueName,
    string? ShareName,
    string? DriverName,
    string? DriverVersion,
    string? PortName,
    string? DeviceAddress,
    string? Location,
    string? Comment,
    PrinterDevice? Device,
    DeviceQueryError? DeviceError);

public sealed record PrintServerSnapshot(
    string Server,
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<PrinterEntry> Printers);
