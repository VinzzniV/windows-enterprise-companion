namespace Wec.Modules.PrintManagement.Domain;

/// <summary>
/// A printer installed on a client (local or a network connection to a print
/// server). Distinct from the server-side <see cref="PrinterEntry"/>: no SNMP
/// device enrichment, this is the client's own printer configuration.
/// </summary>
public sealed record ClientPrinter(
    string Name,
    string? DriverName,
    string? PortName,
    string? Location,
    bool Shared,
    bool IsNetwork);

public sealed record ClientPrinterScan(
    string Host,
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<ClientPrinter> Printers);
