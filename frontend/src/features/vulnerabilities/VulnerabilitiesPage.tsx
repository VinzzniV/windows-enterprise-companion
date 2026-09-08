import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { invoke } from '../../shared/bridge/bridgeClient';
import { presentError, type ErrorPresentation } from '../../shared/bridge/errorPresentation';
import { useSearchParams } from 'react-router-dom';
import type { NessusScan, NessusSyncPhase, NessusSyncStatus, PageResult, VulnerabilityAssetRow, VulnerabilityFindingDetails, VulnerabilityFindingRow, VulnerabilityOverview, VulnerabilityTrend } from '../../shared/api-types';
import { useEnvironment } from '../../shared/environment/EnvironmentContext';
import { Badge } from '../../shared/ui/Badge';
import { Button } from '../../shared/ui/Button';
import { Card } from '../../shared/ui/Card';
import { DataTable, type DataColumn, type DataTableSort } from '../../shared/ui/DataTable';
import { Input } from '../../shared/ui/Input';
import { PageHeader } from '../../shared/ui/PageHeader';
import { Select } from '../../shared/ui/Select';
import { ErrorState } from '../../shared/ui/States';
import { SummaryMetric } from '../../shared/ui/SummaryMetric';
import { SemanticStatusBadge, type SemanticStatus } from '../../shared/ui/SemanticStatusBadge';
import { nessusScanStatus } from './nessusScanStatus';
import { VulnerabilityFindingDetailsPanel } from './VulnerabilityFindingDetailsPanel';

type Tab = 'overview' | 'assets' | 'findings' | 'scans';
const known = (hosts: string[]) => ({ knownHosts: hosts });

const syncPhaseActivityLabels: Record<NessusSyncPhase, string> = {
  IDLE: 'Waiting to synchronize',
  DISCOVERING_SCANS: 'Discovering scans',
  IMPORTING_CURRENT_RUNS: 'Importing current runs',
  PUBLISHING_CURRENT_INVENTORY: 'Publishing current inventory',
  IMPORTING_HISTORY: 'Importing history',
  COMPLETED: 'Synchronization completed',
  FAILED: 'Synchronization failed',
};

export function nessusSyncSemanticStatus(sync: NessusSyncStatus): SemanticStatus {
  if (sync.phase === 'FAILED') return { dimension: 'execution', value: 'failed' };
  if (sync.running) return { dimension: 'execution', value: 'running' };
  if (sync.phase === 'COMPLETED') {
    return sync.error?.trim()
      ? { dimension: 'execution', value: 'partial' }
      : { dimension: 'execution', value: 'succeeded' };
  }
  if (sync.phase === 'IDLE') return { dimension: 'execution', value: 'idle' };
  return { dimension: 'availability', value: 'unknown' };
}

function NessusScanStatusBadge({ scan }: { scan: NessusScan }) {
  const presentation = nessusScanStatus(scan.status, scan.error);
  return <span
    className="inline-flex items-center gap-1.5"
    title={presentation.technicalDetail ?? undefined}
  >
    <SemanticStatusBadge status={presentation.status} />
    {presentation.context && <span className="text-xs text-slate-400">{presentation.context}</span>}
  </span>;
}

type TrendSeverityKey = 'critical' | 'high' | 'medium' | 'low';

export function trendXPositions(
  points: ReadonlyArray<{ dayUtc: string }>,
  width: number,
  left: number,
  right: number,
): number[] {
  const times = points.map((point) => Date.parse(`${point.dayUtc}T00:00:00Z`));
  const valid = times.every(Number.isFinite);
  const minimum = valid ? Math.min(...times) : 0;
  const maximum = valid ? Math.max(...times) : points.length - 1;
  return times.map((time, index) => {
    const value = valid ? time : index;
    return left + (maximum === minimum ? 0 : (value - minimum) * (width - left - right) / (maximum - minimum));
  });
}

function formatTrendDay(dayUtc: string): string {
  return new Intl.DateTimeFormat('en-US', {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    timeZone: 'UTC',
  }).format(new Date(`${dayUtc}T00:00:00Z`));
}

function TrendChart({ trend }: { trend: VulnerabilityTrend }) {
  const width = 900, height = 250, left = 52, right = 20, top = 16, bottom = 40;
  const maximum = Math.max(1, ...trend.points.flatMap((p) => [p.critical, p.high, p.medium, p.low]));
  const plotHeight = height - top - bottom;
  const xPositions = trendXPositions(trend.points, width, left, right);
  const y = (value: number) => height - bottom - value * plotHeight / maximum;
  const path = (key: TrendSeverityKey) => trend.points.map((point, index) => {
    const x = xPositions[index];
    return `${index ? 'L' : 'M'}${x.toFixed(1)},${y(point[key]).toFixed(1)}`;
  }).join(' ');
  const colors = { critical: '#fb7185', high: '#f59e0b', medium: '#60a5fa', low: '#34d399' };
  const ticks = [...new Set([0, Math.ceil(maximum / 2), maximum])].sort((a, b) => a - b);
  return <div className="overflow-x-auto">
    <svg viewBox={`0 0 ${width} ${height}`} className="min-w-[700px]" role="img" aria-label="Nessus severity findings by UTC date">
      {ticks.map((tick) => <g key={tick}>
        <path d={`M${left},${y(tick)}H${width - right}`} stroke="#334155" />
        <text x={left - 8} y={y(tick) + 4} textAnchor="end" fill="#94a3b8" fontSize="11">{tick}</text>
      </g>)}
      <text x="12" y={top + plotHeight / 2} transform={`rotate(-90 12 ${top + plotHeight / 2})`} textAnchor="middle" fill="#94a3b8" fontSize="11">Findings</text>
      <text x={left} y={height - 12} fill="#94a3b8" fontSize="11">{trend.points[0].dayUtc}</text>
      <text x={width - right} y={height - 12} textAnchor="end" fill="#94a3b8" fontSize="11">{trend.points[trend.points.length - 1].dayUtc}</text>
      {(Object.keys(colors) as TrendSeverityKey[]).map((key) => <g key={key}>
        <path d={path(key)} fill="none" stroke={colors[key]} strokeWidth="2" />
        {trend.points.map((point, index) => <circle key={point.dayUtc} cx={xPositions[index]} cy={y(point[key])} r="3" fill={colors[key]}>
          <title>{`${point.dayUtc}: ${key} ${point[key]}`}</title>
        </circle>)}
      </g>)}
    </svg>
    <div className="flex flex-wrap gap-4 text-xs text-slate-400">{Object.entries(colors).map(([label, color]) => <span key={label}><span className="mr-1 inline-block h-2 w-2 rounded-full" style={{ backgroundColor: color }} />{label}</span>)}</div>
    <p className="mt-1 text-xs text-slate-500">UTC date axis · findings per daily snapshot · spacing reflects elapsed days.</p>
  </div>;
}

function DailyTrendValues({ trend }: { trend: VulnerabilityTrend }) {
  return <details className="mt-3 rounded border border-slate-800 bg-slate-950/30">
    <summary className="cursor-pointer px-3 py-2 text-sm text-slate-300">Daily values ({trend.points.length})</summary>
    <div className="overflow-x-auto px-3 pb-3">
      <table className="w-full text-left text-xs text-slate-300">
        <thead><tr className="border-b border-slate-800"><th className="py-1 pr-3">UTC date</th><th className="px-2 py-1 text-right">Critical</th><th className="px-2 py-1 text-right">High</th><th className="px-2 py-1 text-right">Medium</th><th className="px-2 py-1 text-right">Low</th><th className="px-2 py-1 text-right">Assets</th></tr></thead>
        <tbody>{trend.points.map((point) => <tr key={point.dayUtc} className="border-b border-slate-900"><td className="py-1 pr-3"><time dateTime={point.dayUtc}>{formatTrendDay(point.dayUtc)}</time></td><td className="px-2 py-1 text-right">{point.critical}</td><td className="px-2 py-1 text-right">{point.high}</td><td className="px-2 py-1 text-right">{point.medium}</td><td className="px-2 py-1 text-right">{point.low}</td><td className="px-2 py-1 text-right">{point.assets}</td></tr>)}</tbody>
      </table>
    </div>
  </details>;
}

function earliestFollowingTrendDay(trend: VulnerabilityTrend): string | null {
  const latestDay = trend.points.reduce<string | null>(
    (latest, point) => latest === null || point.dayUtc > latest ? point.dayUtc : latest,
    null,
  );
  const parts = latestDay && /^(\d{4})-(\d{2})-(\d{2})$/.exec(latestDay);
  if (!parts) return null;
  const followingDay = new Date(Date.UTC(Number(parts[1]), Number(parts[2]) - 1, Number(parts[3]) + 1));
  return followingDay.toISOString().slice(0, 10);
}

function TrendCard({ trend, days, onDaysChange }: {
  trend: VulnerabilityTrend;
  days: number;
  onDaysChange: (days: number) => void;
}) {
  const periodSelect = <Select fullWidth={false} value={days} onChange={(event) => onDaysChange(Number(event.target.value))}>
    <option value={7}>7 days</option>
    <option value={30}>30 days</option>
    <option value={90}>90 days</option>
  </Select>;

  if (trend.verdict === 'INSUFFICIENT_DATA') {
    const earliestDay = earliestFollowingTrendDay(trend);
    const formattedEarliestDay = earliestDay
      ? new Intl.DateTimeFormat('en-US', { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' })
        .format(new Date(`${earliestDay}T00:00:00Z`))
      : null;
    return <Card title={`${days}-day trend`}>
      <div className="flex flex-wrap items-start gap-3">
        {periodSelect}
        <div className="min-w-0 flex-1">
          <p className="text-sm font-medium text-slate-200">Not enough data for a trend yet</p>
          {earliestDay && formattedEarliestDay
            ? <><p className="mt-1 text-sm text-slate-400">Earliest possible: <time dateTime={earliestDay}>{formattedEarliestDay}</time>, after another snapshot captures at least one previously seen asset.</p>
              <p className="mt-1 text-xs text-slate-500">First daily snapshot: {trend.points[0].critical} Critical, {trend.points[0].high} High, {trend.points[0].medium} Medium, {trend.points[0].low} Low across {trend.points[0].assets} assets.</p></>
            : <p className="mt-1 text-sm text-slate-400">Capture snapshots on two different UTC days with at least one common asset.</p>}
        </div>
      </div>
    </Card>;
  }

  const comparison = trend.comparison!;
  const severityValues = {
    CRITICAL: [comparison.startCritical, comparison.endCritical],
    HIGH: [comparison.startHigh, comparison.endHigh],
    MEDIUM: [comparison.startMedium, comparison.endMedium],
    LOW: [comparison.startLow, comparison.endLow],
  } as const;
  const decidingValues = comparison.decidingSeverity === 'CRITICAL'
    || comparison.decidingSeverity === 'HIGH'
    || comparison.decidingSeverity === 'MEDIUM'
    || comparison.decidingSeverity === 'LOW'
    ? severityValues[comparison.decidingSeverity]
    : null;
  return <Card title={`${days}-day trend · ${trend.verdict.replace('_',' ')}`}>
    <div className="mb-2 flex flex-wrap items-center gap-3 text-xs text-slate-400">{periodSelect}<span>{trend.commonAssets} common assets</span><span>{trend.newAssets} added</span><span>{trend.removedAssets} removed</span></div>
    <div className="mb-3 rounded border border-slate-800 bg-slate-950/30 px-3 py-2 text-sm text-slate-300">
      <p>Verdict compares {formatTrendDay(comparison.startDayUtc)} to {formatTrendDay(comparison.endDayUtc)} using only the {trend.commonAssets} assets present on both dates.</p>
      <p className="mt-1 text-xs text-slate-400">Common-asset findings: Critical {comparison.startCritical} → {comparison.endCritical}; High {comparison.startHigh} → {comparison.endHigh}; Medium {comparison.startMedium} → {comparison.endMedium}; Low {comparison.startLow} → {comparison.endLow}.</p>
      <p className="mt-1 text-xs text-slate-400">
        {comparison.decidingSeverity && decidingValues
          ? `${comparison.decidingSeverity[0]}${comparison.decidingSeverity.slice(1).toLowerCase()} is the first changed severity in the Critical → High → Medium → Low policy (${decidingValues[0]} → ${decidingValues[1]}), so the verdict is ${trend.verdict.toLowerCase()}.`
          : 'All compared severities are unchanged, so the verdict is stable.'}
        {' '}Daily totals below include all assets captured that day; {trend.newAssets} assets were added and {trend.removedAssets} were removed from the end cohort.
      </p>
    </div>
    <TrendChart trend={trend} />
    <DailyTrendValues trend={trend} />
  </Card>;
}

export function VulnerabilitiesPage() {
  const [searchParams] = useSearchParams();
  const assetFilter = searchParams.get('asset')?.trim() ?? '';
  const environment = useEnvironment();
  const requestedTab = searchParams.get('tab');
  const [tab, setTab] = useState<Tab>(requestedTab === 'assets' || requestedTab === 'findings' || requestedTab === 'scans' ? requestedTab : assetFilter ? 'findings' : 'overview');
  const [overview, setOverview] = useState<VulnerabilityOverview | null>(null);
  const [assets, setAssets] = useState<PageResult<VulnerabilityAssetRow> | null>(null);
  const [findings, setFindings] = useState<PageResult<VulnerabilityFindingRow> | null>(null);
  const [scans, setScans] = useState<NessusScan[]>([]);
  const [trend, setTrend] = useState<VulnerabilityTrend | null>(null);
  const [days, setDays] = useState(30);
  const [search, setSearch] = useState('');
  const [severity, setSeverity] = useState('');
  const [overviewError, setOverviewError] = useState<ErrorPresentation | null>(null);
  const [refreshError, setRefreshError] = useState<ErrorPresentation | null>(null);
  const [refreshing, setRefreshing] = useState(false);
  const [tabError, setTabError] = useState<ErrorPresentation | null>(null);
  const [tableLoading, setTableLoading] = useState(false);
  const [detail, setDetail] = useState<VulnerabilityFindingDetails | null>(null);
  const [selectedFindingId, setSelectedFindingId] = useState<number | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [detailError, setDetailError] = useState<ErrorPresentation | null>(null);
  const [assetPage, setAssetPage] = useState(1);
  const [assetPageSize, setAssetPageSize] = useState(50);
  const [assetSort, setAssetSort] = useState<DataTableSort>({ column: 'critical', direction: 'desc' });
  const [findingPage, setFindingPage] = useState(1);
  const [findingPageSize, setFindingPageSize] = useState(50);
  const [findingSort, setFindingSort] = useState<DataTableSort>({ column: 'severity', direction: 'desc' });
  const [dataRevision, setDataRevision] = useState(0);
  const [tabRevision, setTabRevision] = useState(0);
  const autoRefreshAttempted = useRef(false);
  const autoRefreshTimer = useRef<number | null>(null);
  const syncWasRunning = useRef(false);
  const detailRequest = useRef(0);
  const hosts = useMemo(() => environment.result?.devices.map((x) => x.computerName) ?? [], [environment.result]);
  const loadOverview = useCallback(async () => {
    setOverviewError(null);
    try {
      const o = await invoke<VulnerabilityOverview>('vulnerabilitymanagement', 'getOverview', known(hosts));
      setOverview(o);
      if (syncWasRunning.current && !o.sync.running) {
        setDataRevision((current) => current + 1);
        void environment.refresh();
      }
      syncWasRunning.current = o.sync.running;
      const last = o.sync.lastSuccessfulSyncUtc ? Date.now() - new Date(o.sync.lastSuccessfulSyncUtc).getTime() : Number.POSITIVE_INFINITY;
      if (!o.sync.running && last > 15 * 60_000 && !autoRefreshAttempted.current) {
        autoRefreshAttempted.current = true;
        setRefreshError(null);
        try {
          await invoke('vulnerabilitymanagement', 'startSync', {});
          syncWasRunning.current = true;
          autoRefreshTimer.current = window.setTimeout(() => void loadOverview(), 500);
        } catch (caught) {
          setRefreshError(presentError(caught, {
            message: 'The Nessus synchronization could not be started.',
          }));
        }
      }
    } catch (caught) {
      setOverviewError(presentError(caught, {
        message: 'The Nessus vulnerability overview could not be loaded.',
      }));
    }
  }, [environment.refresh, hosts]);

  const startSync = useCallback(async () => {
    setRefreshing(true);
    setRefreshError(null);
    try {
      await invoke('vulnerabilitymanagement', 'startSync', {});
      syncWasRunning.current = true;
      await new Promise((resolve) => window.setTimeout(resolve, 300));
      await loadOverview();
    } catch (caught) {
      setRefreshError(presentError(caught, {
        message: 'The Nessus synchronization could not be started.',
      }));
    } finally {
      setRefreshing(false);
    }
  }, [loadOverview]);
  useEffect(() => { void environment.ensureLoaded(); }, [environment.ensureLoaded]);
  useEffect(() => () => { if (autoRefreshTimer.current !== null) window.clearTimeout(autoRefreshTimer.current); }, []);
  useEffect(() => { void loadOverview(); }, [loadOverview]);
  useEffect(() => { if (!overview?.sync.running) return; const timer = window.setInterval(() => void loadOverview(), 2000); return () => window.clearInterval(timer); }, [loadOverview, overview?.sync.running]);
  useEffect(() => {
    if (tab !== 'overview') return;
    let current = true;
    setTabError(null);
    void invoke<VulnerabilityTrend>('vulnerabilitymanagement', 'getTrend', { days })
      .then((value) => { if (current) setTrend(value); })
      .catch((caught) => { if (current) setTabError(presentError(caught, {
        message: 'The Nessus trend could not be loaded.',
      })); });
    return () => { current = false; };
  }, [days, tab, tabRevision]);
  useEffect(() => {
    if (tab !== 'scans') return;
    let current = true;
    setTabError(null);
    void invoke<NessusScan[]>('vulnerabilitymanagement', 'listScans', {})
      .then((value) => { if (current) setScans(value); })
      .catch((caught) => { if (current) setTabError(presentError(caught, {
        message: 'The Nessus scan list could not be loaded.',
      })); });
    return () => { current = false; };
  }, [dataRevision, tab, tabRevision]);
  useEffect(() => {
    if (tab !== 'assets') return;
    let current = true;
    setTableLoading(true);
    setTabError(null);
    void invoke<PageResult<VulnerabilityAssetRow>>('vulnerabilitymanagement', 'listAssets', {
      ...known(hosts), search, severity: severity || null, page: assetPage, pageSize: assetPageSize,
      sortColumn: assetSort.column, sortDirection: assetSort.direction,
    }).then((value) => { if (current) setAssets(value); })
      .catch((caught) => { if (current) setTabError(presentError(caught, {
        message: 'The Nessus asset list could not be loaded.',
      })); })
      .finally(() => { if (current) setTableLoading(false); });
    return () => { current = false; };
  }, [assetPage, assetPageSize, assetSort, dataRevision, hosts, search, severity, tab, tabRevision]);
  useEffect(() => {
    if (tab !== 'findings') return;
    let current = true;
    setTableLoading(true);
    setTabError(null);
    void invoke<PageResult<VulnerabilityFindingRow>>('vulnerabilitymanagement', 'listFindings', {
      search, severity: severity || null, asset: assetFilter || null, page: findingPage, pageSize: findingPageSize,
      sortColumn: findingSort.column, sortDirection: findingSort.direction,
    }).then((value) => { if (current) setFindings(value); })
      .catch((caught) => { if (current) setTabError(presentError(caught, {
        message: 'The Nessus finding list could not be loaded.',
      })); })
      .finally(() => { if (current) setTableLoading(false); });
    return () => { current = false; };
  }, [assetFilter, dataRevision, findingPage, findingPageSize, findingSort, search, severity, tab, tabRevision]);

  const closeFindingDetails = useCallback(() => {
    detailRequest.current += 1;
    setSelectedFindingId(null);
    setDetail(null);
    setDetailLoading(false);
    setDetailError(null);
  }, []);
  const openFindingDetails = useCallback((finding: VulnerabilityFindingRow) => {
    const request = detailRequest.current + 1;
    detailRequest.current = request;
    setSelectedFindingId(finding.pluginId);
    setDetail(null);
    setDetailLoading(true);
    setDetailError(null);
    void invoke<VulnerabilityFindingDetails | null>('vulnerabilitymanagement', 'getFindingDetails', {
      pluginId: finding.pluginId,
      asset: assetFilter || null,
    }).then((value) => {
      if (detailRequest.current !== request) return;
      if (!value) {
        setDetailError({
          message: 'The selected finding is no longer available.',
          cause: 'The cached finding list no longer matches the current Nessus data.',
          action: 'Refresh the Findings view and select the finding again.',
          technicalDetails: `Plugin ID: ${finding.pluginId}`,
        });
        return;
      }
      setDetail(value);
    }).catch((caught) => {
      if (detailRequest.current === request) {
        setDetailError(presentError(caught, {
          message: 'The selected finding details could not be loaded.',
        }));
      }
    }).finally(() => {
      if (detailRequest.current === request) setDetailLoading(false);
    });
  }, [assetFilter]);

  const assetColumns: DataColumn<VulnerabilityAssetRow>[] = [
    { id: 'asset', header: 'Asset', sortable: true, cell: (x) => <div><div className="font-mono text-slate-100">{x.asset.displayName}</div><div className="text-xs text-muted">{x.asset.ipAddress ?? 'No IP'} · {x.matched ? 'Matched' : 'Unmatched'}</div></div> },
    { id: 'critical', header: 'Critical', sortable: true, cell: (x) => <Badge tone={x.asset.critical ? 'fail' : 'neutral'}>{x.asset.critical}</Badge> },
    { id: 'high', header: 'High', sortable: true, cell: (x) => <Badge tone={x.asset.high ? 'warn' : 'neutral'}>{x.asset.high}</Badge> },
    { id: 'medium', header: 'Medium', sortable: true, cell: (x) => x.asset.medium },
    { id: 'low', header: 'Low', sortable: true, cell: (x) => x.asset.low },
    { id: 'lastScan', header: 'Last scan', sortable: true, cell: (x) => new Date(x.asset.lastScanUtc).toLocaleString() },
  ];
  const findingColumns: DataColumn<VulnerabilityFindingRow>[] = [
    { id: 'finding', header: 'Finding', sortable: true, cell: (x) => <div className="max-w-[36rem] min-w-0"><div className="truncate text-slate-100" title={x.name}>{x.name}</div><div className="font-mono text-xs text-muted">Plugin {x.pluginId} · {x.cves.length} {x.cves.length === 1 ? 'CVE' : 'CVEs'}</div></div> },
    { id: 'severity', header: 'Severity', sortable: true, cell: (x) => <Badge tone={x.severity === 'CRITICAL' ? 'fail' : x.severity === 'HIGH' ? 'warn' : 'neutral'}>{x.severity}</Badge> },
    { id: 'affectedAssets', header: 'Assets', sortable: true, cell: (x) => x.affectedAssets },
    { id: 'instances', header: 'Instances', sortable: true, cell: (x) => x.instances },
  ];
  const scanColumns: DataColumn<NessusScan>[] = [
    { header: 'Scan', cell: (x) => <div><div className="text-slate-100">{x.name}</div><div className="font-mono text-xs text-muted">ID {x.id}</div></div> }, { header: 'Included', cell: (x) => <Badge tone={x.excluded ? 'neutral' : 'ok'}>{x.excluded ? 'Excluded' : 'Included'}</Badge> },
    { header: 'Latest completed run', cell: (x) => x.latestCompletedUtc ? new Date(x.latestCompletedUtc).toLocaleString() : '—' }, { header: 'Status', cell: (x) => <NessusScanStatusBadge scan={x} /> },
  ];
  const sync = overview?.sync;
  const syncError = sync?.error ? presentError(new Error(sync.error), {
    message: 'The latest Nessus synchronization failed.',
    cause: 'Nessus reported an error while WEC synchronized scan data.',
    action: 'Check the Nessus connection and scan status, then retry the synchronization.',
  }) : null;
  const tabErrorTitle = tab === 'overview' ? 'Nessus trend could not be loaded'
    : tab === 'assets' ? 'Nessus assets could not be loaded'
      : tab === 'findings' ? 'Nessus findings could not be loaded'
        : 'Nessus scans could not be loaded';
  const tabRetryLabel = tab === 'overview' ? 'Retry Nessus trend'
    : tab === 'assets' ? 'Retry Nessus assets'
      : tab === 'findings' ? 'Retry Nessus findings'
        : 'Retry Nessus scans';
  return <div className="flex flex-col gap-4"><PageHeader title="Vulnerabilities" subtitle="Read-only Nessus overview across all included scans">
    <Button variant="primary" onClick={() => void startSync()} disabled={sync?.running || refreshing}>{sync?.running || refreshing ? 'Synchronizing…' : 'Refresh Nessus'}</Button>
  </PageHeader>{overviewError && <ErrorState title="Nessus data could not be loaded" {...overviewError} controls={<Button variant="secondary" onClick={() => void loadOverview()}>Retry Nessus overview</Button>} />}
    {refreshError && <ErrorState title="Nessus synchronization could not be started" {...refreshError} controls={<Button variant="secondary" onClick={() => void startSync()}>Retry Nessus synchronization</Button>} />}
    <div className="flex flex-wrap gap-2">{(['overview','assets','findings','scans'] as Tab[]).map((value) => <Button key={value} variant={tab === value ? 'primary' : 'secondary'} onClick={() => { if (value !== 'findings') closeFindingDetails(); setTab(value); }}>{value[0].toUpperCase() + value.slice(1)}</Button>)}</div>
    {sync && <div className="flex flex-wrap items-center gap-2 text-sm text-slate-400"><SemanticStatusBadge status={nessusSyncSemanticStatus(sync)} />{sync.running && <span>{syncPhaseActivityLabels[sync.phase]}</span>}{sync.running && <span>{sync.completedScans} / {sync.totalScans} scans</span>}{sync.lastSuccessfulSyncUtc && <span>Last successful: {new Date(sync.lastSuccessfulSyncUtc).toLocaleString()}</span>}{!sync.historySupported && <span className="text-warn-300">Historical exports are not supported; the trend starts with WEC snapshots.</span>}</div>}
    {syncError && <ErrorState title="Nessus synchronization failed" {...syncError} controls={<Button variant="secondary" onClick={() => void startSync()}>Retry Nessus synchronization</Button>} />}
    {tab === 'overview' && overview && <><div className="flex flex-wrap gap-3"><SummaryMetric label="Included scans" value={overview.includedScans} /><SummaryMetric label="Assets" value={overview.assets} /><SummaryMetric label="Matched" value={overview.matchedAssets} /><SummaryMetric label="Unmatched" value={overview.unmatchedAssets} /><SummaryMetric label="Critical assets" value={overview.criticalAssets} tone={overview.criticalAssets ? 'danger' : 'success'} /><SummaryMetric label="High assets" value={overview.highAssets} tone={overview.highAssets ? 'warning' : 'success'} /><SummaryMetric label="Critical instances" value={overview.criticalInstances} tone={overview.criticalInstances ? 'danger' : 'success'} /><SummaryMetric label="High instances" value={overview.highInstances} tone={overview.highInstances ? 'warning' : 'success'} /></div>
      {trend && <TrendCard trend={trend} days={days} onDaysChange={setDays} />}</>}
    {tab === 'findings' && assetFilter && <div className="rounded border border-accent-800/60 bg-accent-950/20 px-3 py-2 text-sm text-slate-300">Showing Nessus findings for <span className="font-mono text-slate-100">{assetFilter}</span>.</div>}
    {(tab === 'assets' || tab === 'findings') && <div className="flex gap-3"><Input type="search" value={search} onChange={(e) => { closeFindingDetails(); setSearch(e.target.value); setAssetPage(1); setFindingPage(1); }} placeholder="Search host, plugin or CVE…" /><Select fullWidth={false} value={severity} onChange={(e) => { closeFindingDetails(); setSeverity(e.target.value); setAssetPage(1); setFindingPage(1); }}><option value="">All severities</option><option>CRITICAL</option><option>HIGH</option><option>MEDIUM</option><option>LOW</option><option>UNKNOWN</option></Select></div>}
    {tabError && <ErrorState title={tabErrorTitle} {...tabError} controls={<Button variant="secondary" onClick={() => setTabRevision((current) => current + 1)}>{tabRetryLabel}</Button>} />}
    {tab === 'assets' && <Card title={`Assets (${assets?.total ?? '…'})`}><DataTable columns={assetColumns} rows={assets?.items ?? []} getRowKey={(x) => x.asset.assetKey} emptyMessage="No Nessus assets match the filters." loading={tableLoading} sort={assetSort} onSortChange={(value) => { setAssetSort(value); setAssetPage(1); }} pagination={{ page: assets?.page ?? assetPage, pageSize: assets?.pageSize ?? assetPageSize, total: assets?.total ?? 0, onPageChange: setAssetPage, onPageSizeChange: (value) => { setAssetPageSize(value); setAssetPage(1); } }} /></Card>}
    {tab === 'findings' && <div className={`grid min-w-0 gap-4 ${selectedFindingId !== null ? 'xl:grid-cols-[minmax(0,1fr)_minmax(24rem,0.7fr)]' : ''}`}>
      <Card title={`Findings (${findings?.total ?? '…'})`}><DataTable columns={findingColumns} rows={findings?.items ?? []} getRowKey={(x) => String(x.pluginId)} onRowClick={openFindingDetails} isRowActive={(x) => x.pluginId === selectedFindingId} emptyMessage="No Nessus findings match the filters." loading={tableLoading} sort={findingSort} onSortChange={(value) => { closeFindingDetails(); setFindingSort(value); setFindingPage(1); }} pagination={{ page: findings?.page ?? findingPage, pageSize: findings?.pageSize ?? findingPageSize, total: findings?.total ?? 0, onPageChange: (value) => { closeFindingDetails(); setFindingPage(value); }, onPageSizeChange: (value) => { closeFindingDetails(); setFindingPageSize(value); setFindingPage(1); } }} /></Card>
      {detailLoading && <section role="region" aria-label="Finding details" className="self-start rounded-lg border border-slate-700 bg-slate-900 p-4 text-sm text-slate-400"><p role="status">Loading finding details…</p></section>}
      {detailError && !detailLoading && <section role="region" aria-label="Finding details" className="self-start rounded-lg border border-slate-700 bg-slate-900 p-4"><div className="mb-3 flex justify-end"><Button variant="ghost" aria-label="Close finding details" onClick={closeFindingDetails}>Close</Button></div><ErrorState title="Finding details could not be loaded" {...detailError} controls={selectedFindingId !== null ? <Button variant="secondary" onClick={() => { const finding = findings?.items.find((item) => item.pluginId === selectedFindingId); if (finding) openFindingDetails(finding); }}>Retry finding details</Button> : undefined} /></section>}
      {detail && !detailLoading && !detailError && <VulnerabilityFindingDetailsPanel detail={detail} onClose={closeFindingDetails} />}
    </div>}
    {tab === 'scans' && <Card title={`Scans (${scans.length})`}><DataTable columns={scanColumns} rows={scans} getRowKey={(x) => String(x.id)} emptyMessage="No visible Nessus scans are cached." /></Card>}
  </div>;
}
