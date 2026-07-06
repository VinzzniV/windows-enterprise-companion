using Wec.Modules.PrintManagement.Domain;

namespace Wec.Modules.PrintManagement.Application;

public sealed record LeaseDiffDevice(
    string SerialNumber, string? Model, string? QueueName, string? DeviceAddress);

public sealed record LeaseQueueSwap(
    string QueueName, string OldSerialNumber, string NewSerialNumber, string? OldModel, string? NewModel);

public sealed record PrintServerDiff(
    string Server,
    DateTimeOffset BaselineAtUtc,
    DateTimeOffset LatestAtUtc,
    IReadOnlyList<LeaseDiffDevice> NewDevices,
    IReadOnlyList<LeaseDiffDevice> GoneDevices,
    IReadOnlyList<LeaseQueueSwap> SwappedQueues,
    int DevicesWithoutSerialNumber);

/// <summary>
/// Serial-number-based comparison between two snapshots — the lease-renewal
/// view: which devices are new, which are gone, which queue got a different
/// physical device. Devices whose serial could not be read (unreachable,
/// no SNMP) are counted, not silently ignored.
/// </summary>
internal static class LeaseDiff
{
    internal static PrintServerDiff Compute(PrintServerSnapshot baseline, PrintServerSnapshot latest)
    {
        Dictionary<string, PrinterEntry> baselineBySerial = EntriesBySerial(baseline);
        Dictionary<string, PrinterEntry> latestBySerial = EntriesBySerial(latest);

        var swaps = new List<LeaseQueueSwap>();
        foreach (PrinterEntry latestEntry in latest.Printers)
        {
            PrinterEntry? baselineEntry = baseline.Printers.FirstOrDefault(entry =>
                string.Equals(entry.QueueName, latestEntry.QueueName, StringComparison.OrdinalIgnoreCase));
            string? oldSerial = baselineEntry?.Device?.SerialNumber;
            string? newSerial = latestEntry.Device?.SerialNumber;
            if (oldSerial is not null && newSerial is not null
                && !string.Equals(oldSerial, newSerial, StringComparison.OrdinalIgnoreCase))
            {
                swaps.Add(new LeaseQueueSwap(
                    latestEntry.QueueName,
                    oldSerial,
                    newSerial,
                    baselineEntry!.Device!.Model,
                    latestEntry.Device!.Model));
            }
        }

        var swappedNewSerials = new HashSet<string>(
            swaps.Select(swap => swap.NewSerialNumber), StringComparer.OrdinalIgnoreCase);
        var swappedOldSerials = new HashSet<string>(
            swaps.Select(swap => swap.OldSerialNumber), StringComparer.OrdinalIgnoreCase);

        List<LeaseDiffDevice> newDevices = [.. latestBySerial
            .Where(pair => !baselineBySerial.ContainsKey(pair.Key) && !swappedNewSerials.Contains(pair.Key))
            .Select(pair => ToDiffDevice(pair.Key, pair.Value))
            .OrderBy(device => device.SerialNumber, StringComparer.OrdinalIgnoreCase)];
        List<LeaseDiffDevice> goneDevices = [.. baselineBySerial
            .Where(pair => !latestBySerial.ContainsKey(pair.Key) && !swappedOldSerials.Contains(pair.Key))
            .Select(pair => ToDiffDevice(pair.Key, pair.Value))
            .OrderBy(device => device.SerialNumber, StringComparer.OrdinalIgnoreCase)];

        int withoutSerial = latest.Printers.Count(entry =>
            entry.DeviceAddress is not null && entry.Device?.SerialNumber is null);

        return new PrintServerDiff(
            latest.Server,
            baseline.CapturedAtUtc,
            latest.CapturedAtUtc,
            newDevices,
            goneDevices,
            [.. swaps.OrderBy(swap => swap.QueueName, StringComparer.OrdinalIgnoreCase)],
            withoutSerial);
    }

    private static Dictionary<string, PrinterEntry> EntriesBySerial(PrintServerSnapshot snapshot)
    {
        var bySerial = new Dictionary<string, PrinterEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (PrinterEntry entry in snapshot.Printers)
        {
            if (entry.Device?.SerialNumber is { Length: > 0 } serial)
            {
                // Two queues can point at the same physical device; one entry suffices
                bySerial.TryAdd(serial, entry);
            }
        }

        return bySerial;
    }

    private static LeaseDiffDevice ToDiffDevice(string serial, PrinterEntry entry) =>
        new(serial, entry.Device?.Model, entry.QueueName, entry.DeviceAddress);
}
