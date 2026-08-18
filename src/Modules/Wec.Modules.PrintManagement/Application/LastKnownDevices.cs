using Wec.Modules.PrintManagement.Domain;
using Wec.Modules.PrintManagement.Persistence;

namespace Wec.Modules.PrintManagement.Application;

/// <summary>
/// A device that did not answer this time keeps the identity it reported in an
/// earlier scan (serial, model, sysName, location), so one unreachable printer no
/// longer blanks out its row and the CSV export. Fresh data always wins — the
/// carried values are only used where the current scan has none.
///
/// Deliberately applied on read, never persisted: stored snapshots stay pure
/// measurements, otherwise the serial-based lease diff would never report a
/// device as gone and the consistency hints would go quiet. Volatile values
/// (status, toner, page count) are dropped — a stale toner level is a lie.
/// </summary>
internal sealed class LastKnownDevices
{
    private readonly IPrintSnapshotRepository _repository;

    public LastKnownDevices(IPrintSnapshotRepository repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// Walks the server's history from newest to oldest until every unreachable
    /// queue has its last known device data (or the history is exhausted).
    /// </summary>
    public async Task<PrintServerSnapshot> FillAsync(
        PrintServerSnapshot snapshot, CancellationToken cancellationToken)
    {
        PrintServerSnapshot filled = snapshot;
        foreach (PrintSnapshotStamp stamp in await _repository.GetHistoryAsync(
            snapshot.Server, cancellationToken))
        {
            if (!filled.Printers.Any(NeedsDeviceData))
            {
                break;
            }

            if (stamp.CapturedAtUtc >= snapshot.CapturedAtUtc)
            {
                continue;
            }

            if (await _repository.GetByIdAsync(stamp.Id, cancellationToken) is { } older)
            {
                filled = Fill(filled, older);
            }
        }

        return filled;
    }

    internal static bool NeedsDeviceData(PrinterEntry entry) =>
        entry.Device is null && entry.DeviceAddress is not null;

    internal static PrintServerSnapshot Fill(PrintServerSnapshot target, PrintServerSnapshot older)
    {
        var byQueue = new Dictionary<string, PrinterEntry>(StringComparer.OrdinalIgnoreCase);
        var byAddress = new Dictionary<string, PrinterEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (PrinterEntry entry in older.Printers.Where(entry => entry.Device is not null))
        {
            byQueue.TryAdd(entry.QueueName, entry);
            if (entry.DeviceAddress is { Length: > 0 } address)
            {
                byAddress.TryAdd(address, entry);
            }
        }

        return target with
        {
            Printers =
            [
                .. target.Printers.Select(entry =>
                {
                    if (!NeedsDeviceData(entry) || FindKnown(entry, byQueue, byAddress) is not { } known)
                    {
                        return entry;
                    }

                    return entry with
                    {
                        Device = known.Device! with { Status = null, PageCount = null, Supplies = [] },
                        DeviceDataFromUtc = older.CapturedAtUtc,
                    };
                }),
            ],
        };
    }

    // The queue name is the stable identity across scans; the port address covers
    // a renamed queue that still points at the same device.
    private static PrinterEntry? FindKnown(
        PrinterEntry entry,
        IReadOnlyDictionary<string, PrinterEntry> byQueue,
        IReadOnlyDictionary<string, PrinterEntry> byAddress) =>
        byQueue.GetValueOrDefault(entry.QueueName)
        ?? (entry.DeviceAddress is { Length: > 0 } address ? byAddress.GetValueOrDefault(address) : null);
}
