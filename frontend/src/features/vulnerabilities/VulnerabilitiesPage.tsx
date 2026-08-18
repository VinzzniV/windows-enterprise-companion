import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { invoke } from '../../shared/bridge/bridgeClient';
import { useSearchParams } from 'react-router-dom';
import type { NessusScan, PageResult, VulnerabilityAssetRow, VulnerabilityFindingDetails, VulnerabilityFindingRow, VulnerabilityOverview, VulnerabilityTrend } from '../../shared/api-types';
import { useEnvironment } from '../../shared/environment/EnvironmentContext';
import { Badge } from '../../shared/ui/Badge';
import { Button } from '../../shared/ui/Button';
import { Card } from '../../shared/ui/Card';
import { DataTable, type DataColumn } from '../../shared/ui/DataTable';
import { Input } from '../../shared/ui/Input';
import { PageHeader } from '../../shared/ui/PageHeader';
import { Select } from '../../shared/ui/Select';
import { ErrorState } from '../../shared/ui/States';
import { SummaryMetric } from '../../shared/ui/SummaryMetric';

type Tab = 'overview' | 'assets' | 'findings' | 'scans';
const known = (hosts: string[]) => ({ knownHosts: hosts });

function TrendChart({ trend }: { trend: VulnerabilityTrend }) {
  const width = 900, height = 210, pad = 28;
  const maximum = Math.max(1, ...trend.points.flatMap((p) => [p.critical, p.high, p.medium, p.low]));
  const path = (key: 'critical' | 'high' | 'medium' | 'low') => trend.points.map((point, index) => {
    const x = pad + (trend.points.length < 2 ? 0 : index * (width - pad * 2) / (trend.points.length - 1));
    const y = height - pad - point[key] * (height - pad * 2) / maximum;
    return `${index ? 'L' : 'M'}${x.toFixed(1)},${y.toFixed(1)}`;
  }).join(' ');
  const colors = { critical: '#fb7185', high: '#f59e0b', medium: '#60a5fa', low: '#34d399' };
  return <div className="overflow-x-auto"><svg viewBox={`0 0 ${width} ${height}`} className="min-w-[700px]" role="img" aria-label="Nessus severity trend">
    <path d={`M${pad},${height - pad}H${width - pad}`} stroke="#334155" />
    {(Object.keys(colors) as Array<keyof typeof colors>).map((key) => <path key={key} d={path(key)} fill="none" stroke={colors[key]} strokeWidth="2" />)}
  </svg><div className="flex gap-4 text-xs text-slate-400">{Object.entries(colors).map(([label, color]) => <span key={label}><span className="mr-1 inline-block h-2 w-2 rounded-full" style={{ backgroundColor: color }} />{label}</span>)}</div></div>;
}

export function VulnerabilitiesPage() {
  const [searchParams] = useSearchParams();
  const assetFilter = searchParams.get('asset')?.trim() ?? '';
  const environment = useEnvironment();
  const requestedTab = searchParams.get('tab');
  const [tab, setTab] = useState<Tab>(requestedTab === 'assets' || requestedTab === 'findings' || requestedTab === 'scans' ? requestedTab : assetFilter ? 'findings' : 'overview'); const [overview, setOverview] = useState<VulnerabilityOverview | null>(null);
  const [assets, setAssets] = useState<PageResult<VulnerabilityAssetRow> | null>(null); const [findings, setFindings] = useState<PageResult<VulnerabilityFindingRow> | null>(null);
  const [scans, setScans] = useState<NessusScan[]>([]); const [trend, setTrend] = useState<VulnerabilityTrend | null>(null); const [days, setDays] = useState(30);
  const [search, setSearch] = useState(''); const [severity, setSeverity] = useState(''); const [error, setError] = useState<string | null>(null); const [detail, setDetail] = useState<VulnerabilityFindingDetails | null>(null);
  const autoRefreshAttempted = useRef(false);
  const autoRefreshTimer = useRef<number | null>(null);
  const hosts = useMemo(() => environment.result?.devices.map((x) => x.computerName) ?? [], [environment.result]);
  const load = useCallback(async () => {
    setError(null);
    try {
      const [o, a, f, s, t] = await Promise.all([
        invoke<VulnerabilityOverview>('vulnerabilitymanagement', 'getOverview', known(hosts)),
        invoke<PageResult<VulnerabilityAssetRow>>('vulnerabilitymanagement', 'listAssets', { ...known(hosts), search, severity: severity || null, page: 1, pageSize: 250 }),
        invoke<PageResult<VulnerabilityFindingRow>>('vulnerabilitymanagement', 'listFindings', { search, severity: severity || null, asset: assetFilter || null, page: 1, pageSize: 250 }),
        invoke<NessusScan[]>('vulnerabilitymanagement', 'listScans', {}), invoke<VulnerabilityTrend>('vulnerabilitymanagement', 'getTrend', { days }),
      ]); setOverview(o); setAssets(a); setFindings(f); setScans(s); setTrend(t);
      const last = o.sync.lastSuccessfulSyncUtc ? Date.now() - new Date(o.sync.lastSuccessfulSyncUtc).getTime() : Number.POSITIVE_INFINITY;
      if (!o.sync.running && last > 15 * 60_000 && !autoRefreshAttempted.current) {
        autoRefreshAttempted.current = true;
        await invoke('vulnerabilitymanagement', 'startSync', {});
        autoRefreshTimer.current = window.setTimeout(() => void load(), 500);
      }
    } catch (caught) { setError(caught instanceof Error ? caught.message : String(caught)); }
  }, [assetFilter, days, hosts, search, severity]);
  useEffect(() => { void environment.ensureLoaded(); }, [environment.ensureLoaded]);
  useEffect(() => () => { if (autoRefreshTimer.current !== null) window.clearTimeout(autoRefreshTimer.current); }, []);
  useEffect(() => { void load(); }, [load]);
  useEffect(() => { if (!overview?.sync.running) return; const timer = window.setInterval(() => void load(), 2000); return () => window.clearInterval(timer); }, [load, overview?.sync.running]);

  const assetColumns: DataColumn<VulnerabilityAssetRow>[] = [
    { header: 'Asset', cell: (x) => <div><div className="font-mono text-slate-100">{x.asset.displayName}</div><div className="text-xs text-slate-500">{x.asset.ipAddress ?? 'No IP'} · {x.matched ? 'Matched' : 'Unmatched'}</div></div> },
    { header: 'Critical', cell: (x) => <Badge tone={x.asset.critical ? 'fail' : 'neutral'}>{x.asset.critical}</Badge> }, { header: 'High', cell: (x) => <Badge tone={x.asset.high ? 'warn' : 'neutral'}>{x.asset.high}</Badge> },
    { header: 'Medium', cell: (x) => x.asset.medium }, { header: 'Low', cell: (x) => x.asset.low }, { header: 'Last scan', cell: (x) => new Date(x.asset.lastScanUtc).toLocaleString() },
  ];
  const findingColumns: DataColumn<VulnerabilityFindingRow>[] = [
    { header: 'Finding', cell: (x) => <div><div className="text-slate-100">{x.name}</div><div className="font-mono text-xs text-slate-500">Plugin {x.pluginId}{x.cves.length ? ` · ${x.cves.join(', ')}` : ''}</div></div> },
    { header: 'Severity', cell: (x) => <Badge tone={x.severity === 'CRITICAL' ? 'fail' : x.severity === 'HIGH' ? 'warn' : 'neutral'}>{x.severity}</Badge> }, { header: 'Assets', cell: (x) => x.affectedAssets }, { header: 'Instances', cell: (x) => x.instances },
  ];
  const scanColumns: DataColumn<NessusScan>[] = [
    { header: 'Scan', cell: (x) => <div><div className="text-slate-100">{x.name}</div><div className="font-mono text-xs text-slate-500">ID {x.id}</div></div> }, { header: 'Included', cell: (x) => <Badge tone={x.excluded ? 'neutral' : 'ok'}>{x.excluded ? 'Excluded' : 'Included'}</Badge> },
    { header: 'Latest completed run', cell: (x) => x.latestCompletedUtc ? new Date(x.latestCompletedUtc).toLocaleString() : '—' }, { header: 'Status', cell: (x) => x.error ? <Badge tone="fail">Error</Badge> : <Badge tone="neutral">{x.status ?? 'Unknown'}</Badge> },
  ];
  const sync = overview?.sync;
  return <div className="flex flex-col gap-4"><PageHeader title="Vulnerabilities" subtitle="Read-only Nessus overview across all included scans">
    <Button variant="primary" onClick={() => invoke('vulnerabilitymanagement', 'startSync', {}).then(() => new Promise((resolve) => window.setTimeout(resolve, 300))).then(() => load())} disabled={sync?.running}>{sync?.running ? 'Synchronizing…' : 'Refresh Nessus'}</Button>
  </PageHeader>{error && <ErrorState title="Nessus data could not be loaded" message={error} />}
    <div className="flex flex-wrap gap-2">{(['overview','assets','findings','scans'] as Tab[]).map((value) => <Button key={value} variant={tab === value ? 'primary' : 'secondary'} onClick={() => setTab(value)}>{value[0].toUpperCase() + value.slice(1)}</Button>)}</div>
    {sync && <div className="flex flex-wrap items-center gap-2 text-sm text-slate-400"><Badge tone={sync.phase === 'FAILED' ? 'fail' : sync.running ? 'warn' : 'ok'}>{sync.phase.replaceAll('_',' ')}</Badge>{sync.running && <span>{sync.completedScans} / {sync.totalScans} scans</span>}{sync.lastSuccessfulSyncUtc && <span>Last successful: {new Date(sync.lastSuccessfulSyncUtc).toLocaleString()}</span>}{!sync.historySupported && <span className="text-warn-300">Historical exports are not supported; the trend starts with WEC snapshots.</span>}{sync.error && <span className="text-warn-300">{sync.error}</span>}</div>}
    {tab === 'overview' && overview && <><div className="flex flex-wrap gap-3"><SummaryMetric label="Included scans" value={overview.includedScans} /><SummaryMetric label="Assets" value={overview.assets} /><SummaryMetric label="Matched" value={overview.matchedAssets} /><SummaryMetric label="Unmatched" value={overview.unmatchedAssets} /><SummaryMetric label="Critical assets" value={overview.criticalAssets} tone={overview.criticalAssets ? 'danger' : 'success'} /><SummaryMetric label="High assets" value={overview.highAssets} tone={overview.highAssets ? 'warning' : 'success'} /><SummaryMetric label="Critical instances" value={overview.criticalInstances} tone={overview.criticalInstances ? 'danger' : 'success'} /><SummaryMetric label="High instances" value={overview.highInstances} tone={overview.highInstances ? 'warning' : 'success'} /></div>
      {trend && <Card title={`${days}-day trend · ${trend.verdict.replace('_',' ')}`}><div className="mb-2 flex items-center gap-3 text-xs text-slate-400"><Select fullWidth={false} value={days} onChange={(e) => setDays(Number(e.target.value))}><option value={7}>7 days</option><option value={30}>30 days</option><option value={90}>90 days</option></Select><span>{trend.commonAssets} common</span><span>{trend.newAssets} new</span><span>{trend.removedAssets} removed</span></div><TrendChart trend={trend} /></Card>}</>}
    {tab === 'findings' && assetFilter && <div className="rounded border border-accent-800/60 bg-accent-950/20 px-3 py-2 text-sm text-slate-300">Showing Nessus findings for <span className="font-mono text-slate-100">{assetFilter}</span>.</div>}
    {(tab === 'assets' || tab === 'findings') && <div className="flex gap-3"><Input type="search" value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search host, plugin or CVE…" /><Select fullWidth={false} value={severity} onChange={(e) => setSeverity(e.target.value)}><option value="">All severities</option><option>CRITICAL</option><option>HIGH</option><option>MEDIUM</option><option>LOW</option></Select></div>}
    {tab === 'assets' && assets && <Card title={`Assets (${assets.total})`}><DataTable columns={assetColumns} rows={assets.items} getRowKey={(x) => x.asset.assetKey} emptyMessage="No Nessus assets match the filters." /></Card>}
    {tab === 'findings' && findings && <Card title={`Findings (${findings.total})`}><DataTable columns={findingColumns} rows={findings.items} getRowKey={(x) => String(x.pluginId)} onRowClick={(x) => invoke<VulnerabilityFindingDetails>('vulnerabilitymanagement','getFindingDetails',{ pluginId: x.pluginId, asset: assetFilter || null }).then(setDetail)} emptyMessage="No Nessus findings match the filters." /></Card>}
    {detail && tab === 'findings' && <Card title={`${detail.name} · Plugin ${detail.pluginId}`}><p className="mb-2 text-sm text-slate-300">{detail.synopsis ?? 'No synopsis provided.'}</p><p className="text-sm text-slate-400"><span className="font-semibold text-slate-300">Solution:</span> {detail.solution ?? '—'}</p><p className="mt-2 text-xs text-slate-500">{detail.instances.length} deduplicated instance(s) · {detail.cves.join(', ') || 'No CVE'}</p><div className="mt-3 overflow-x-auto"><table className="w-full text-left text-xs"><thead className="text-slate-500"><tr><th className="py-1">Asset</th><th>Port</th><th>Protocol</th><th>Source scans</th></tr></thead><tbody>{detail.instances.slice(0, 50).map((instance) => <tr key={`${instance.assetKey}-${instance.port}-${instance.protocol}`} className="border-t border-slate-800 text-slate-300"><td className="py-1 font-mono">{instance.assetKey}</td><td>{instance.port || '—'}</td><td>{instance.protocol || '—'}</td><td>{instance.scanSources.join(', ')}</td></tr>)}</tbody></table></div></Card>}
    {tab === 'scans' && <Card title={`Scans (${scans.length})`}><DataTable columns={scanColumns} rows={scans} getRowKey={(x) => String(x.id)} emptyMessage="No visible Nessus scans are cached." /></Card>}
  </div>;
}
