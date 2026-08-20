import type {
  OpsiConnectionStatusResult,
  PatchWorkflowState,
  PrintServerSnapshot,
  SecurityScanResult,
  StoredInventoryHost,
  StoredPrintServer,
  VulnerabilityOverview,
  VulnerabilityTrend,
} from '../../shared/api-types';
import type { MetricTone } from '../../shared/ui/SummaryMetric';

export type DashboardDataState =
  | 'loading'
  | 'fresh'
  | 'stale'
  | 'missing'
  | 'partial'
  | 'error'
  | 'unknown';

export interface TileMetric {
  value: string;
  tone: MetricTone;
  state: DashboardDataState;
  note: string;
  source: string;
  capturedAtUtc?: string;
  coverage: string;
}

function formatTimestamp(iso: string): string {
  return new Date(iso).toLocaleString();
}

function latestOf<T extends { capturedAtUtc: string }>(items: T[]): T {
  return items.reduce((newest, item) => (
    Date.parse(item.capturedAtUtc) > Date.parse(newest.capturedAtUtc) ? item : newest
  ));
}

function freshnessWindowLabel(maximumAgeSeconds: number): string {
  if (maximumAgeSeconds % 3600 === 0) return `${maximumAgeSeconds / 3600}h`;
  if (maximumAgeSeconds % 60 === 0) return `${maximumAgeSeconds / 60}m`;
  return `${maximumAgeSeconds}s`;
}

function timestampFreshness(
  capturedAtUtc: string,
  maximumAgeSeconds: number,
  evaluatedAt: Date,
): 'fresh' | 'stale' | 'unknown' {
  const capturedAt = Date.parse(capturedAtUtc);
  const evaluatedAtMs = evaluatedAt.getTime();
  if (!Number.isFinite(capturedAt) || capturedAt > evaluatedAtMs) return 'unknown';
  return evaluatedAtMs - capturedAt > maximumAgeSeconds * 1000 ? 'stale' : 'fresh';
}

export function loadingTile(source: string): TileMetric {
  return {
    value: 'Loading…',
    tone: 'neutral',
    state: 'loading',
    note: 'Reading the latest source state',
    source,
    coverage: 'Not evaluated yet',
  };
}

export function errorTile(source: string, note: string): TileMetric {
  return {
    value: 'Unavailable',
    tone: 'danger',
    state: 'error',
    note,
    source,
    coverage: 'Source could not be evaluated',
  };
}

export function deriveInventoryTile(
  hosts: StoredInventoryHost[],
  maximumAgeSeconds: number,
  evaluatedAt = new Date(),
): TileMetric {
  const source = 'Stored WMI/CIM inventory snapshots';
  if (hosts.length === 0) {
    return {
      value: 'No data',
      tone: 'neutral',
      state: 'missing',
      note: 'Run an inventory scan',
      source,
      coverage: '0 hosts with stored inventory',
    };
  }
  const latest = latestOf(hosts);
  const states = hosts.map((host) => timestampFreshness(host.capturedAtUtc, maximumAgeSeconds, evaluatedAt));
  const fresh = states.filter((state) => state === 'fresh').length;
  const stale = states.filter((state) => state === 'stale').length;
  const unknown = states.length - fresh - stale;
  const state: DashboardDataState = unknown > 0
    ? 'unknown'
    : fresh === hosts.length
      ? 'fresh'
      : stale === hosts.length
        ? 'stale'
        : 'partial';
  const window = freshnessWindowLabel(maximumAgeSeconds);
  return {
    value: `${hosts.length} host${hosts.length === 1 ? '' : 's'}`,
    tone: state === 'fresh' ? 'neutral' : state === 'unknown' ? 'neutral' : 'warning',
    state,
    note: state === 'stale'
      ? `All captures are older than the ${window} freshness window`
      : `Last capture ${formatTimestamp(latest.capturedAtUtc)}`,
    source,
    capturedAtUtc: latest.capturedAtUtc,
    coverage: `${fresh} of ${hosts.length} hosts within ${window} freshness window`,
  };
}

export function deriveSecurityTile(
  scan: SecurityScanResult | null,
  maximumAgeSeconds: number,
  evaluatedAt = new Date(),
): TileMetric {
  const source = 'Persisted Security scan and per-check outcomes';
  if (!scan) {
    return {
      value: 'No data',
      tone: 'neutral',
      state: 'missing',
      note: 'Run a security scan',
      source,
      coverage: 'No security checks evaluated',
    };
  }
  const problems = scan.findings;
  const critical = problems.filter((finding) => finding.severity === 'CRITICAL').length;
  const high = problems.filter((finding) => finding.severity === 'HIGH').length;
  const attention = critical + high;
  const freshness = timestampFreshness(scan.completedAtUtc, maximumAgeSeconds, evaluatedAt);
  const state: DashboardDataState = scan.status === 'FAILED'
    ? 'error'
    : freshness === 'unknown'
      ? 'unknown'
      : freshness === 'stale'
        ? 'stale'
        : scan.coverage.isKnown && scan.coverage.isComplete && scan.status === 'COMPLETED'
          ? 'fresh'
          : 'partial';
  const window = freshnessWindowLabel(maximumAgeSeconds);
  const coverage = scan.coverage.isKnown
    ? `${scan.coverage.succeededChecks} of ${scan.coverage.applicableChecks} applicable checks evaluated`
    : 'Per-check coverage unavailable';
  if (state === 'error') {
    return {
      value: 'Unavailable',
      tone: 'danger',
      state,
      note: `Latest scan for ${scan.host} failed`,
      source,
      capturedAtUtc: scan.completedAtUtc,
      coverage,
    };
  }
  return {
    value: attention > 0 ? `${attention} critical/high` : `${problems.length} findings`,
    tone: critical > 0 ? 'danger' : high > 0 || state !== 'fresh' ? 'warning' : 'success',
    state,
    note: state === 'stale'
      ? `${scan.host} · older than the ${window} freshness window`
      : `${scan.host} · ${formatTimestamp(scan.completedAtUtc)} · ${
        scan.coverage.isComplete ? 'coverage complete' : 'coverage incomplete'
      }`,
    source,
    capturedAtUtc: scan.completedAtUtc,
    coverage,
  };
}

export function derivePrintTile(
  servers: StoredPrintServer[],
  snapshots: PrintServerSnapshot[],
  failedSnapshots = 0,
): TileMetric {
  const source = 'Stored print-server snapshots';
  if (servers.length === 0) {
    return {
      value: 'No data',
      tone: 'neutral',
      state: 'missing',
      note: 'Scan a print server',
      source,
      coverage: '0 registered print servers',
    };
  }
  const printers = snapshots.reduce((sum, snapshot) => sum + snapshot.printers.length, 0);
  const tonerLow = snapshots.reduce(
    (sum, snapshot) =>
      sum +
      snapshot.printers.filter((printer) => printer.device?.supplies.some((supply) => supply.isLow))
        .length,
    0,
  );
  const latest = snapshots.length > 0 ? latestOf(snapshots) : null;
  const complete = failedSnapshots === 0 && snapshots.length === servers.length;
  return {
    value: `${servers.length} server${servers.length === 1 ? '' : 's'}`,
    tone: tonerLow > 0 ? 'warning' : 'neutral',
    state: complete ? 'unknown' : 'partial',
    note: tonerLow > 0
      ? `${tonerLow} printer(s) low on toner`
      : complete
        ? `${printers} printers · no freshness policy configured`
        : `${printers} printers from the available snapshots`,
    source,
    capturedAtUtc: latest?.capturedAtUtc,
    coverage: `${snapshots.length} of ${servers.length} servers with readable snapshots`,
  };
}

export function derivePatchTile(
  status: OpsiConnectionStatusResult | null,
  evaluatedAt = new Date(),
): TileMetric {
  const source = 'Live opsi connection status';
  if (!status) return errorTile(source, 'Could not read the opsi connection state');
  if (!status.connected) {
    return {
      value: 'Not connected',
      tone: 'neutral',
      state: 'missing',
      note: status.connectionError ?? 'Connect to opsi',
      source,
      coverage: 'No live opsi session',
    };
  }
  return {
    value: 'Connected',
    tone: 'success',
    state: 'fresh',
    note: status.serverUrl ?? 'Live opsi session',
    source,
    capturedAtUtc: evaluatedAt.toISOString(),
    coverage: 'Connection verified during this dashboard load',
  };
}

export function deriveVulnerabilityTile(
  overview: VulnerabilityOverview,
  trend: VulnerabilityTrend | null,
): TileMetric {
  const source = 'Persisted Nessus scan inventory';
  const { sync } = overview;
  if (sync.running) {
    return {
      value: 'Syncing',
      tone: 'info',
      state: 'loading',
      note: sync.phase.replaceAll('_', ' ').toLowerCase(),
      source,
      capturedAtUtc: sync.lastSuccessfulSyncUtc ?? undefined,
      coverage: `${sync.completedScans} of ${sync.totalScans} scans imported`,
    };
  }
  if (sync.phase === 'FAILED' || sync.error) {
    return {
      ...errorTile(source, sync.error ?? 'The last Nessus sync failed'),
      capturedAtUtc: sync.lastSuccessfulSyncUtc ?? undefined,
    };
  }
  if (!sync.lastSuccessfulSyncUtc || overview.includedScans === 0) {
    return {
      value: 'No data',
      tone: 'neutral',
      state: 'missing',
      note: 'Run a Nessus sync',
      source,
      capturedAtUtc: sync.lastSuccessfulSyncUtc ?? undefined,
      coverage: 'No included completed scans',
    };
  }

  const state: DashboardDataState = overview.staleScans >= overview.includedScans
    ? 'stale'
    : overview.staleScans > 0 || trend === null
      ? 'partial'
      : 'fresh';
  const verdict = trend?.verdict ?? 'INSUFFICIENT_DATA';
  const trendLabel = verdict === 'BETTER' ? '↓ better' : verdict === 'WORSE' ? '↑ worse' : `→ ${verdict.replace('_', ' ').toLowerCase()}`;
  return {
    value: String(overview.criticalAssets),
    tone: overview.criticalAssets > 0 ? 'danger' : overview.highAssets > 0 || state !== 'fresh' ? 'warning' : 'success',
    state,
    note: `${overview.highAssets} high · ${trendLabel}`,
    source,
    capturedAtUtc: sync.lastSuccessfulSyncUtc,
    coverage: trend === null
      ? `${overview.includedScans} included scans · trend unavailable`
      : `${overview.includedScans} included scans · ${overview.staleScans} stale`,
  };
}

export interface PatchChartSegment {
  label: string;
  count: number;
  colorClass: string;
}

/**
 * Partition the opsi products by workflow state for the dashboard chart.
 * Every product lands in exactly one bucket; empty buckets are dropped.
 */
export function derivePatchStatusChart(
  products: readonly { state: PatchWorkflowState }[],
): PatchChartSegment[] {
  const buckets = { failed: 0, outdated: 0, inProgress: 0, upToDate: 0, noState: 0 };
  for (const product of products) {
    if (product.state === 'FAILED') buckets.failed += 1;
    else if (product.state === 'UPDATE_AVAILABLE') buckets.outdated += 1;
    else if (product.state === 'COMPLETED') buckets.upToDate += 1;
    else if (product.state === 'DETECTED') buckets.noState += 1;
    else buckets.inProgress += 1; // the prepared/approved/rollout states
  }
  return [
    { label: 'Failed', count: buckets.failed, colorClass: 'bg-fail-500' },
    { label: 'Update available', count: buckets.outdated, colorClass: 'bg-warn-500' },
    { label: 'In progress', count: buckets.inProgress, colorClass: 'bg-info-500' },
    { label: 'Up to date', count: buckets.upToDate, colorClass: 'bg-ok-500' },
    { label: 'No client state', count: buckets.noState, colorClass: 'bg-slate-600' },
  ].filter((segment) => segment.count > 0);
}
