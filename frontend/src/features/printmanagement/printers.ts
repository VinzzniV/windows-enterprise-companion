import type {
  DeviceQueryError,
  NetworkPolicyResult,
  PrinterEntry,
  TonerSupply,
} from '../../shared/api-types';
import { printerObservationGroupLabel } from './printStatus';

/**
 * Strip the variant suffixes a print queue name carries after its running number
 * (PK-NETPRT002_B_PCL → PK-NETPRT002). One physical device is exposed as several
 * queues that differ only by suffix: paper size (_A5), colour (_B/_C), driver
 * language (_PCL/_PS/_KX) or a plain counter (_2). A suffix without an underscore
 * only strips after a digit, so ordinary names ending in B/C are left alone.
 *
 * ponytail: a whitelist, not "cut everything after the last _" — the latter would
 * rename real queues like CNIPF770_KST. Add tokens here when new ones show up.
 */
const VARIANT_SUFFIX =
  /(?:_(?:A5|A4|BLACK|COLOR|PCL|PCLXL|PS|KX|B|C|Q|\d{1,2})|(?<=\d)(?:A5|A4|BLACK|COLOR|B|C))$/i;

export function baseQueueName(queueName: string): string {
  let name = queueName.trim();
  for (let previous = ''; name !== previous; ) {
    previous = name;
    name = name.replace(VARIANT_SUFFIX, '');
  }
  return name === '' ? queueName.trim() : name;
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
  portName: string | null;
  /** Host address the port points at (IP or hostname), from the print server. */
  portAddress: string | null;
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
  /** Resolved IPv4 of the device, from the print server's port (a hostname otherwise). */
  deviceIp: string | null;
  /** Location display value: server-preferred, kept for search/grouping (see displayedLocation for the truth). */
  location: string | null;
  /** SNMP sysLocation reported by the device itself — the source of truth. */
  deviceLocation: string | null;
  /** Location label configured on the print server queue. */
  serverLocation: string | null;
  /** Whether at least one queue reached the physical device over SNMP in this scan. */
  deviceAnswered: boolean;
  /** Set when the device data is carried over from an earlier scan: its capture time. */
  deviceDataFromUtc: string | null;
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
    // Freshly measured data wins over data carried over from an earlier scan
    const answered = group.find((item) => item.entry.device != null && !item.entry.deviceDataFromUtc);
    const withDevice = answered ?? group.find((item) => item.entry.device != null);
    const device = withDevice?.entry.device ?? null;
    const addressEntry = group.find((item) => item.entry.deviceAddress != null)?.entry;
    // Empty-string locations must not shadow a real SNMP sysLocation (server-scan
    // queues surface a blank Location as '' rather than null).
    const nonEmpty = (value: string | null | undefined): value is string =>
      value != null && value.trim() !== '';
    const serverLocation = group.map((item) => item.entry.location).find(nonEmpty) ?? null;
    const deviceLocation = group.map((item) => item.entry.device?.sysLocation).find(nonEmpty) ?? null;
    const location = serverLocation ?? deviceLocation;
    const deviceIp = group.map((item) => item.entry.deviceIp).find(nonEmpty) ?? null;
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
        portName: item.entry.portName,
        portAddress: item.entry.deviceAddress,
      })),
      model: device?.model ?? null,
      serialNumber: device?.serialNumber ?? null,
      deviceAddress: addressEntry?.deviceAddress ?? null,
      deviceIp,
      location,
      deviceLocation,
      serverLocation,
      deviceAnswered: answered != null,
      deviceDataFromUtc: answered ? null : withDevice?.entry.deviceDataFromUtc ?? null,
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
      printer.deviceLocation ?? '',
      printer.model ?? '',
      printer.deviceAddress ?? '',
      printer.deviceIp ?? '',
      ...printer.queues.map((queue) => queue.queueName),
    ].some((field) => field.toLowerCase().includes(needle)),
  );
}

/** The location to show: the device's own SNMP value (truth) when it answered, else the print server's. */
export function displayedLocation(printer: MergedPrinter): string | null {
  return printer.deviceAnswered ? printer.deviceLocation : printer.serverLocation;
}

/**
 * When the device answered SNMP, flag how its location (the truth) diverges from
 * the print server label — nothing to flag when they agree or the device is unreachable.
 */
export function locationFlag(
  printer: MergedPrinter,
): { reason: 'missing' | 'mismatch'; serverLocation: string } | null {
  if (!printer.deviceAnswered) return null;
  const device = (printer.deviceLocation ?? '').trim();
  const server = (printer.serverLocation ?? '').trim();
  if (device === '') {
    return server === '' ? null : { reason: 'missing', serverLocation: server };
  }
  if (device.toLowerCase() !== server.toLowerCase()) {
    return { reason: 'mismatch', serverLocation: server === '' ? '(empty)' : server };
  }
  return null;
}

export interface PrinterGroup {
  label: string;
  printers: MergedPrinter[];
}

export type PrinterGroupMode = 'none' | 'site' | 'status' | 'server' | 'model';

function groupLabel(printer: MergedPrinter, mode: PrinterGroupMode): string {
  switch (mode) {
    case 'site':
      return printer.site;
    case 'status':
      return printerObservationGroupLabel(printer.deviceError?.code ?? null, printer.status);
    case 'server':
      // A device merged across servers forms its own combined group — rare and honest.
      return printer.servers.join(' + ');
    case 'model':
      return printer.model ?? 'Unknown model';
    default:
      return '';
  }
}

/** Group (and thereby sort) the printers by the chosen dimension; 'none' keeps one flat group. */
export function groupPrinters(
  printers: readonly MergedPrinter[],
  mode: PrinterGroupMode,
): PrinterGroup[] {
  if (mode === 'none') {
    return [{ label: '', printers: [...printers] }];
  }
  const groups = new Map<string, MergedPrinter[]>();
  for (const printer of printers) {
    const label = groupLabel(printer, mode);
    const bucket = groups.get(label);
    if (bucket) bucket.push(printer);
    else groups.set(label, [printer]);
  }
  return [...groups.entries()]
    .sort((a, b) => a[0].localeCompare(b[0]))
    .map(([label, grouped]) => ({ label, printers: grouped }));
}

function ipv4ToInt(ip: string): number | null {
  const parts = ip.trim().split('.');
  if (parts.length !== 4) return null;
  let value = 0;
  for (const part of parts) {
    if (!/^\d{1,3}$/.test(part)) return null;
    const octet = Number(part);
    if (octet > 255) return null;
    value = value * 256 + octet;
  }
  return value >>> 0;
}

/** True when an IPv4 address falls inside a CIDR block (e.g. '172.20.20.0/24'). */
export function ipInCidr(ip: string, cidr: string): boolean {
  const [base, bitsText] = cidr.split('/');
  const bits = Number(bitsText);
  const ipInt = ipv4ToInt(ip);
  const baseInt = ipv4ToInt(base ?? '');
  if (ipInt === null || baseInt === null || !Number.isInteger(bits) || bits < 0 || bits > 32) {
    return false;
  }
  if (bits === 0) return true;
  const mask = (0xffffffff << (32 - bits)) >>> 0;
  return (ipInt & mask) === (baseInt & mask);
}

export type SubnetClass = 'target' | 'legacy' | 'foreign' | 'unknown';

/**
 * Places a device address relative to the network policy: the target printer
 * subnet, an old subnet to migrate away from, a foreign VLAN, or unknown (no IP).
 */
export function classifySubnet(ip: string | null, policy: NetworkPolicyResult): SubnetClass {
  if (!ip) return 'unknown';
  if (policy.printerSubnets.some((cidr) => ipInCidr(ip, cidr))) return 'target';
  if (policy.legacySubnets.some((cidr) => ipInCidr(ip, cidr))) return 'legacy';
  return 'foreign';
}

/** Lowest toner percent across supplies, or null when none report a level. */
export function lowestTonerPercent(supplies: readonly TonerSupply[]): number | null {
  const levels = supplies.map((supply) => supply.percent).filter((p): p is number => p != null);
  return levels.length > 0 ? Math.min(...levels) : null;
}

export function hasLowToner(supplies: readonly TonerSupply[]): boolean {
  return supplies.some((supply) => supply.isLow);
}
