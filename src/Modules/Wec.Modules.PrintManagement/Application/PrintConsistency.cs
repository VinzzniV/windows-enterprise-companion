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
}
