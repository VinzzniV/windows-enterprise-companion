using System.Globalization;
using System.Text;
using Wec.Modules.PrintManagement.Domain;

namespace Wec.Modules.PrintManagement.Application;

/// <summary>
/// The recurring report as CSV — semicolon-separated (opens correctly in a
/// German-locale Excel), one row per queue.
/// </summary>
internal static class PrintCsvExport
{
    internal static string BuildCsv(IReadOnlyList<PrintServerSnapshot> snapshots)
    {
        var builder = new StringBuilder();
        builder.AppendLine(
            "Server;Queue;SerialNumber;Location;DeviceLocation;Model;Status;Toner;IPAddress;Driver;DriverVersion;CapturedAtUtc");
        foreach (PrintServerSnapshot snapshot in snapshots)
        {
            foreach (PrinterEntry entry in snapshot.Printers)
            {
                string toner = string.Join(" | ", (entry.Device?.Supplies ?? [])
                    .Select(supply => supply.Percent is { } percent
                        ? $"{supply.Description} {percent}%"
                        : supply.Description));
                builder.AppendLine(string.Join(';',
                    new[]
                    {
                        snapshot.Server,
                        entry.QueueName,
                        entry.Device?.SerialNumber,
                        entry.Location,
                        entry.Device?.SysLocation,
                        entry.Device?.Model,
                        entry.Device?.Status,
                        toner,
                        entry.DeviceAddress,
                        entry.DriverName,
                        entry.DriverVersion,
                        snapshot.CapturedAtUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    }.Select(EscapeCsvField)));
            }
        }

        return builder.ToString();
    }

    internal static string EscapeCsvField(string? field)
    {
        if (string.IsNullOrEmpty(field))
        {
            return string.Empty;
        }

        return field.Contains(';', StringComparison.Ordinal)
            || field.Contains('"', StringComparison.Ordinal)
            || field.Contains('\n', StringComparison.Ordinal)
            || field.Contains('\r', StringComparison.Ordinal)
            ? "\"" + field.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : field;
    }
}
