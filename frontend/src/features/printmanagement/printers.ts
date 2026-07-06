import type { DeviceQueryError, PrinterEntry, TonerSupply } from '../../shared/api-types';

/**
 * Strip the variant suffix a print queue name carries after its running number
 * (KF-NETPRT001 → base). One physical device is exposed as several queues that
 * differ only by suffix: _A5 (paper size), B/Black, C/Color. Only strip when a
 * digit precedes the suffix so ordinary names ending in B/C are left alone.
 */
export function baseQueueName(queueName: string): string {
  const match = /^(.*\d)(?:_?(?:A5|BLACK|COLOR|B|C))$/i.exec(queueName.trim());
  return match ? match[1] : queueName.trim();
}

/** Site code = the name prefix before the first '-' (KF/PK/MA/KW/SU …), else "Other". */
export function siteOf(name: string): string {
  const dash = name.indexOf('-');
  if (dash <= 0) return 'Other';
  return name.slice(0, dash).trim().toUpperCase() || 'Other';
}

export interface QueueRef {
  server: string;
  queueName: string;
  shareName: string | null;
  driverName: string | null;
  driverVersion: string | null;
}

/** One physical device, aggregating the queues that point at it. */
export interface MergedPrinter {
  key: string;
  name: string;
  site: string;
  servers: string[];
  queues: QueueRef[];
  model: string | null;
  serialNumber: string | null;
  deviceAddress: string | null;
  location: string | null;
  status: string | null;
  supplies: TonerSupply[];
  deviceError: DeviceQueryError | null;
}

export interface ServerEntry {
  server: string;
  entry: PrinterEntry;
}

/** Merge key: serial → IP → server+base name (strongest identity available). */
function mergeKey({ server, entry }: ServerEntry): string {
  const serial = entry.device?.serialNumber?.trim();
  if (serial) return `serial:${serial.toUpperCase()}`;
  const address = entry.deviceAddress?.trim();
  if (address) return `ip:${address.toUpperCase()}`;
  return `name:${server.toUpperCase()}:${baseQueueName(entry.queueName).toUpperCase()}`;
}

/** Collapse queues that belong to the same physical device into one row. */
export function mergePrinters(entries: readonly ServerEntry[]): MergedPrinter[] {
  const groups = new Map<string, ServerEntry[]>();
  for (const item of entries) {
    const key = mergeKey(item);
    const bucket = groups.get(key);
    if (bucket) bucket.push(item);
    else groups.set(key, [item]);
  }

  const merged: MergedPrinter[] = [];
  for (const [key, group] of groups) {
    const withDevice = group.find((item) => item.entry.device != null);
    const device = withDevice?.entry.device ?? null;
    const addressEntry = group.find((item) => item.entry.deviceAddress != null)?.entry;
    const located = group.find((item) => item.entry.location ?? item.entry.device?.sysLocation)?.entry;
    const errored = group.find((item) => item.entry.deviceError != null)?.entry;
    const name = baseQueueName(group[0].entry.queueName);

    merged.push({
      key,
      name,
      site: siteOf(name),
      servers: [...new Set(group.map((item) => item.server))],
      queues: group.map((item) => ({
        server: item.server,
        queueName: item.entry.queueName,
        shareName: item.entry.shareName,
        driverName: item.entry.driverName,
        driverVersion: item.entry.driverVersion,
      })),
      model: device?.model ?? null,
      serialNumber: device?.serialNumber ?? null,
      deviceAddress: addressEntry?.deviceAddress ?? null,
      location: located?.location ?? located?.device?.sysLocation ?? null,
      status: device?.status ?? null,
      supplies: device?.supplies ?? [],
      deviceError: device ? null : errored?.deviceError ?? null,
    });
  }

  return merged.sort((a, b) => a.name.localeCompare(b.name));
}

export function filterPrinters(printers: readonly MergedPrinter[], term: string): MergedPrinter[] {
  const needle = term.trim().toLowerCase();
  if (needle === '') return [...printers];
  return printers.filter((printer) =>
    [
      printer.name,
      printer.serialNumber ?? '',
      printer.location ?? '',
      printer.model ?? '',
      printer.deviceAddress ?? '',
      ...printer.queues.map((queue) => queue.queueName),
    ].some((field) => field.toLowerCase().includes(needle)),
  );
}

export interface PrinterSiteGroup {
  site: string;
  printers: MergedPrinter[];
}

export function groupBySite(printers: readonly MergedPrinter[]): PrinterSiteGroup[] {
  const groups = new Map<string, MergedPrinter[]>();
  for (const printer of printers) {
    const bucket = groups.get(printer.site);
    if (bucket) bucket.push(printer);
    else groups.set(printer.site, [printer]);
  }
  return [...groups.entries()]
    .sort((a, b) => a[0].localeCompare(b[0]))
    .map(([site, grouped]) => ({ site, printers: grouped }));
}

/** Lowest toner percent across supplies, or null when none report a level. */
export function lowestTonerPercent(supplies: readonly TonerSupply[]): number | null {
  const levels = supplies.map((supply) => supply.percent).filter((p): p is number => p != null);
  return levels.length > 0 ? Math.min(...levels) : null;
}

export function hasLowToner(supplies: readonly TonerSupply[]): boolean {
  return supplies.some((supply) => supply.isLow);
}
