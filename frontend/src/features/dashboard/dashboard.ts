import type {
  OpsiConnectionStatusResult,
  PrintServerSnapshot,
  SecurityScanResult,
  StoredInventoryHost,
  StoredPrintServer,
} from '../../shared/api-types';
import type { MetricTone } from '../../shared/ui/SummaryMetric';

export interface TileMetric {
  value: string;
  tone: MetricTone;
  note?: string;
}

function formatTimestamp(iso: string): string {
  return new Date(iso).toLocaleString();
}

function latestOf<T extends { capturedAtUtc: string }>(items: T[]): T {
  return items.reduce((newest, item) => (item.capturedAtUtc > newest.capturedAtUtc ? item : newest));
}

/** Coverage checks (skipped remotely / read failed) are not security problems. */
function isProblem(findingId: string): boolean {
  return !findingId.endsWith('-LOCAL-ONLY') && !findingId.endsWith('-NOT-RUN');
}

export function deriveInventoryTile(hosts: StoredInventoryHost[]): TileMetric {
  if (hosts.length === 0) {
    return { value: 'No hosts', tone: 'neutral', note: 'Run an inventory scan' };
  }
  return {
    value: `${hosts.length} host${hosts.length === 1 ? '' : 's'}`,
    tone: 'neutral',
    note: `Last capture ${formatTimestamp(latestOf(hosts).capturedAtUtc)}`,
  };
}

export function deriveSecurityTile(scan: SecurityScanResult | null): TileMetric {
  if (!scan) {
    return { value: 'No scan', tone: 'neutral', note: 'Run a security scan' };
  }
  const problems = scan.findings.filter((finding) => isProblem(finding.findingId));
  const critical = problems.filter((finding) => finding.severity === 'CRITICAL').length;
  const high = problems.filter((finding) => finding.severity === 'HIGH').length;
  const attention = critical + high;
  return {
    value: attention > 0 ? `${attention} critical/high` : `${problems.length} findings`,
    tone: critical > 0 ? 'danger' : high > 0 ? 'warning' : 'success',
    note: `${scan.host} · ${formatTimestamp(scan.completedAtUtc)}`,
  };
}

export function derivePrintTile(
  servers: StoredPrintServer[],
  snapshots: PrintServerSnapshot[],
): TileMetric {
  if (servers.length === 0) {
    return { value: 'No servers', tone: 'neutral', note: 'Scan a print server' };
  }
  const printers = snapshots.reduce((sum, snapshot) => sum + snapshot.printers.length, 0);
  const tonerLow = snapshots.reduce(
    (sum, snapshot) =>
      sum +
      snapshot.printers.filter((printer) => printer.device?.supplies.some((supply) => supply.isLow))
        .length,
    0,
  );
  return {
    value: `${servers.length} server${servers.length === 1 ? '' : 's'}`,
    tone: tonerLow > 0 ? 'warning' : 'neutral',
    note: tonerLow > 0 ? `${tonerLow} printer(s) low on toner` : `${printers} printers`,
  };
}

export function derivePatchTile(status: OpsiConnectionStatusResult | null): TileMetric {
  if (!status || !status.connected) {
    return { value: 'Not connected', tone: 'neutral', note: 'Connect to opsi' };
  }
  return {
    value: 'Connected',
    tone: 'success',
    note: status.serverUrl ?? undefined,
  };
}
