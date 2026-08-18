using Wec.Modules.PrintManagement.Domain;

namespace Wec.Modules.PrintManagement.Application;

public sealed record PrintHint(string Category, string Message);

/// <summary>
/// Lightweight consistency hints computed from the captured snapshots — no
/// extra scan, rendered like the coverage notes on the Security page.
/// </summary>
internal static class PrintConsistency
{
    internal static IReadOnlyList<PrintHint> ComputeHints(
        IReadOnlyList<PrintServerSnapshot> snapshots, string configuredCommunity)
    {
        var hints = new List<PrintHint>();

        foreach (PrintServerSnapshot snapshot in snapshots)
        {
            foreach (PrinterEntry entry in snapshot.Printers
                .Where(entry => entry.DeviceAddress is not null && entry.DeviceError is not null))
            {
                hints.Add(new PrintHint(
                    "Unreachable device",
                    $"{snapshot.Server}: queue '{entry.QueueName}' points at {entry.DeviceAddress} "
                    + $"but the device did not answer ({entry.DeviceError!.Code}) — orphaned queue "
                    + "or SNMP blocked."));
            }
        }

        foreach (PrintServerSnapshot snapshot in snapshots)
        {
            foreach (PrinterEntry entry in snapshot.Printers.Where(entry => entry.Device is not null))
            {
                string deviceLocation = entry.Device!.SysLocation?.Trim() ?? string.Empty;
                string serverLocation = entry.Location?.Trim() ?? string.Empty;

                if (deviceLocation.Length == 0)
                {
                    hints.Add(new PrintHint(
                        "Device location missing",
                        $"{snapshot.Server}\\{entry.QueueName}: the printer reports no location over SNMP; "
                        + $"the print server says '{DescribeLocation(serverLocation)}'. Set the location on the device."));
                }
                else if (!string.Equals(deviceLocation, serverLocation, StringComparison.OrdinalIgnoreCase))
                {
                    hints.Add(new PrintHint(
                        "Device location mismatch",
                        $"{snapshot.Server}\\{entry.QueueName}: print server says '{DescribeLocation(serverLocation)}' but "
                        + $"the printer (source of truth) says '{deviceLocation}'. Update the print server to match the device."));
                }
            }
        }

        foreach (PrintServerSnapshot snapshot in snapshots)
        {
            foreach (PrinterEntry entry in snapshot.Printers
                .Where(entry => entry.Device?.Model is { Length: > 0 } && entry.DriverName is { Length: > 0 }))
            {
                if (!DriverLikelyMatchesModel(entry.Device!.Model!, entry.DriverName!))
                {
                    hints.Add(new PrintHint(
                        "Driver may not match model",
                        $"{snapshot.Server}\\{entry.QueueName}: the device reports model '{entry.Device.Model}' but the "
                        + $"installed driver is '{entry.DriverName}' — likely a leftover after a hardware swap. "
                        + "Verify the driver fits (heuristic — universal drivers are ignored)."));
                }
            }
        }

        foreach (PrintServerSnapshot snapshot in snapshots.Where(snapshot => snapshot.UnusedDrivers.Count > 0))
        {
            string names = string.Join(", ", snapshot.UnusedDrivers.Take(8).Select(driver => driver.Name));
            hints.Add(new PrintHint(
                "Unused driver",
                $"{snapshot.Server}: {snapshot.UnusedDrivers.Count} installed driver(s) not used by any queue: {names}"
                + (snapshot.UnusedDrivers.Count > 8 ? ", …" : ".")
                + " Remove them on the server if no longer needed."));
        }

        foreach (var driverGroup in snapshots
            .SelectMany(snapshot => snapshot.Printers)
            .Where(entry => entry.DriverName is not null && entry.DriverVersion is not null)
            .GroupBy(entry => entry.DriverName!, StringComparer.OrdinalIgnoreCase))
        {
            List<string> versions = [.. driverGroup
                .Select(entry => entry.DriverVersion!)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)];
            if (versions.Count > 1)
            {
                hints.Add(new PrintHint(
                    "Driver version spread",
                    $"Driver '{driverGroup.Key}' is installed in {versions.Count} versions "
                    + $"({string.Join(", ", versions)}) across the scanned servers."));
            }
        }

        List<string> unlabeled = [.. snapshots
            .SelectMany(snapshot => snapshot.Printers
                .Where(entry => string.IsNullOrWhiteSpace(entry.Location) && string.IsNullOrWhiteSpace(entry.Comment))
                .Select(entry => $"{snapshot.Server}\\{entry.QueueName}"))];
        if (unlabeled.Count > 0)
        {
            string examples = string.Join(", ", unlabeled.Take(5));
            hints.Add(new PrintHint(
                "Missing location/comment",
                $"{unlabeled.Count} queue(s) have neither a location nor a comment: {examples}"
                + (unlabeled.Count > 5 ? ", …" : ".")));
        }

        if (string.Equals(configuredCommunity, "public", StringComparison.OrdinalIgnoreCase))
        {
            hints.Add(new PrintHint(
                "Default SNMP community",
                "The configured SNMP read community is the default 'public' — every device on the "
                + "network can be read by anyone. Consider a custom read community on the devices "
                + "and in Wec:PrintManagement:SnmpCommunity."));
        }

        return hints;
    }

    private static string DescribeLocation(string value) => value.Length == 0 ? "(empty)" : value;

    // ponytail: model↔driver matching is inherently fuzzy — SNMP model strings and
    // Windows driver names don't align across vendors. This only flags the clear case
    // (the device's distinctive model code appears nowhere in the driver name), skips
    // universal drivers, and stays silent when there is nothing distinctive to match.
    // Upgrade path: a per-vendor model→driver map if false negatives matter.
    internal static bool DriverLikelyMatchesModel(string model, string driverName)
    {
        if (driverName.Contains("universal", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        List<string> modelCodes = [.. Tokenize(model).Where(HasDigit)];
        if (modelCodes.Count == 0)
        {
            return true;
        }

        List<string> driverTokens = [.. Tokenize(driverName).Where(HasDigit)];
        return modelCodes.Any(code => driverTokens.Any(token => SharePrefix(code, token, 4)));
    }

    private static string[] Tokenize(string value) =>
        value.ToUpperInvariant().Split(
            [' ', '-', '_', '/', '\\', '(', ')', '[', ']', '.', ',', ':'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool HasDigit(string token) => token.Any(char.IsDigit);

    private static bool SharePrefix(string a, string b, int length)
    {
        if (a.Length < length || b.Length < length)
        {
            return a.Equals(b, StringComparison.Ordinal);
        }

        return a.AsSpan(0, length).SequenceEqual(b.AsSpan(0, length));
    }
}
