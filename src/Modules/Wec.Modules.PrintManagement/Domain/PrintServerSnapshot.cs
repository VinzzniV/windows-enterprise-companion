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
    DeviceQueryError? DeviceError)
{
    /// <summary>
    /// Resolved IPv4 of <see cref="DeviceAddress"/>, which is frequently a DNS
    /// hostname (the print server's port name). Null when it did not resolve.
    /// </summary>
    public string? DeviceIp { get; init; }

    /// <summary>
    /// Set when <see cref="Device"/> was carried over from an earlier scan because
    /// the device did not answer this time: the capture time of that scan. Null on
    /// freshly measured data. Never persisted — filled on read (see LastKnownDevices).
    /// </summary>
    public DateTimeOffset? DeviceDataFromUtc { get; init; }
}

/// <summary>
/// A TCP/IP printer port on the server that no queue uses anymore — safe to
/// delete during cleanup. System pseudo-ports (FILE:, LPT1:, nul:, PORTPROMPT:)
/// are excluded because they carry no host address.
/// </summary>
public sealed record UnusedPort(string Name, string? HostAddress);

/// <summary>
/// A printer driver installed on the server that no queue references anymore —
/// a cleanup candidate (unlike ports, deletion is out of scope: drivers have
/// package dependencies and are risky to remove blind, so this is surfaced as a
/// hint only).
/// </summary>
public sealed record UnusedDriver(string Name, string? Version);

public sealed record PrintServerSnapshot(
    string Server,
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<PrinterEntry> Printers)
{
    public IReadOnlyList<UnusedPort> UnusedPorts { get; init; } = [];

    public IReadOnlyList<UnusedDriver> UnusedDrivers { get; init; } = [];
}
