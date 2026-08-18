import { useCallback, useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { invoke } from '../../shared/bridge/bridgeClient';
import type { HostProbe, HygieneDevice, HygieneFindingCode, InventorySourceState, ListInventoryHostsResult, ProbeHostsResult, StoredInventoryHost } from '../../shared/api-types';
import { useEnvironment } from '../../shared/environment/EnvironmentContext';
import { useTargets } from '../../shared/targets/TargetContext';
import { PageHeader } from '../../shared/ui/PageHeader';
import { Toolbar } from '../../shared/ui/Toolbar';
import { Input } from '../../shared/ui/Input';
import { Select } from '../../shared/ui/Select';
import { Button } from '../../shared/ui/Button';
import { Badge, type BadgeTone } from '../../shared/ui/Badge';
import { DataTable, type DataColumn } from '../../shared/ui/DataTable';
import { EmptyState, ErrorState } from '../../shared/ui/States';
import { Spinner } from '../../shared/ui/Spinner';
import { buildClientList, filterClients, groupClients, type ClientEntry, type GroupMode } from './clients';

type ClientStatusFilter = 'ALL' | 'HEALTHY' | 'PROBLEMS' | 'INCOMPLETE' | 'UNMANAGED';
type ClientSourceFilter = 'ALL' | 'AD' | 'KASPERSKY' | 'OPSI' | 'NESSUS' | 'SCANNED' | 'SAVED';

function hasFinding(device: HygieneDevice, codes: HygieneFindingCode[]) {
  return device.assessment.findings.some((finding) => codes.includes(finding.code));
}

function findingStatus(device: HygieneDevice, codes: HygieneFindingCode[], label: string): { label: string; tone: BadgeTone } {
  const findings = device.assessment.findings.filter((finding) => codes.includes(finding.code));
  if (!findings.length) return { label: 'OK', tone: 'ok' };
  return {
    label,
    tone: findings.some((finding) => finding.severity === 'CRITICAL') ? 'fail' : 'warn',
  };
}

function availabilityLabel(state: InventorySourceState) {
  return ({ AVAILABLE: 'Available', NOT_CONNECTED: 'Not connected', UNAVAILABLE: 'Unavailable', TRUNCATED: 'Truncated', PARTIAL: 'Partial' })[state.availability];
}

function SourceBadge({ client, state, source }: { client: ClientEntry; state: InventorySourceState; source: 'ad' | 'ksc' | 'opsi' | 'nessus' }) {
  const device = client.environment;
  if (!device) return <Badge tone="neutral">Not inventoried</Badge>;
  if (state.availability !== 'AVAILABLE') return <Badge tone="neutral">{availabilityLabel(state)}</Badge>;
  if (source === 'ad') {
    if (!device.activeDirectory.exists) return <Badge tone={hasFinding(device, ['ORPHAN_KASPERSKY', 'ORPHAN_OPSI']) ? 'warn' : 'neutral'}>{hasFinding(device, ['ORPHAN_KASPERSKY', 'ORPHAN_OPSI']) ? 'Missing' : 'N/A'}</Badge>;
    if (!device.activeDirectory.enabled) return <Badge tone="neutral">Disabled</Badge>;
    const status = findingStatus(device, ['STALE_AD'], 'Stale');
    return <Badge tone={status.tone}>{status.label}</Badge>;
  }
  if (source === 'ksc') {
    if (!device.kaspersky.exists) return <Badge tone={hasFinding(device, ['MISSING_KASPERSKY']) ? 'warn' : 'neutral'}>{hasFinding(device, ['MISSING_KASPERSKY']) ? 'Missing' : 'N/A'}</Badge>;
    const stale = hasFinding(device, ['STALE_KASPERSKY']);
    const status = findingStatus(
      device,
      ['STALE_KASPERSKY', 'OUTDATED_AGENT', 'OUTDATED_KES'],
      stale ? 'Stale' : 'Outdated',
    );
    return <Badge tone={status.tone}>{status.label}</Badge>;
  }
  if (source === 'opsi') {
    if (!device.opsi.exists) return <Badge tone={hasFinding(device, ['MISSING_OPSI']) ? 'warn' : 'neutral'}>{hasFinding(device, ['MISSING_OPSI']) ? 'Missing' : 'N/A'}</Badge>;
    const status = findingStatus(device, ['STALE_OPSI'], 'Stale');
    return <Badge tone={status.tone}>{status.label}</Badge>;
  }
  if (!device.nessus.exists) return <Badge tone={hasFinding(device, ['MISSING_NESSUS']) ? 'warn' : 'neutral'}>{hasFinding(device, ['MISSING_NESSUS']) ? 'Missing' : 'N/A'}</Badge>;
  if (hasFinding(device, ['NESSUS_CRITICAL_VULNERABILITIES'])) return <Badge tone="fail">Critical</Badge>;
  if (hasFinding(device, ['NESSUS_HIGH_VULNERABILITIES'])) return <Badge tone="warn">High</Badge>;
  const status = findingStatus(device, ['STALE_NESSUS'], 'Stale');
  return <Badge tone={status.tone}>{status.label}</Badge>;
}

function overall(client: ClientEntry): { label: string; tone: BadgeTone } {
  const status = client.environment?.assessment.status;
  if (!status) return { label: 'Unmanaged', tone: 'neutral' };
  if (status === 'HEALTHY') return { label: 'Healthy', tone: 'ok' };
  if (status === 'CLEANUP_CANDIDATE') return { label: 'Cleanup candidate', tone: 'fail' };
  if (status === 'CRITICAL') return { label: 'Critical', tone: 'fail' };
  if (status === 'INCOMPLETE') return { label: 'Incomplete', tone: 'neutral' };
  return { label: 'Warning', tone: 'warn' };
}

function matchesStatus(client: ClientEntry, filter: ClientStatusFilter) {
  if (filter === 'ALL') return true;
  if (!client.environment) return filter === 'UNMANAGED';
  if (filter === 'HEALTHY') return client.environment.assessment.status === 'HEALTHY';
  if (filter === 'INCOMPLETE') return client.environment.assessment.status === 'INCOMPLETE';
  if (filter === 'PROBLEMS') return ['WARNING', 'CLEANUP_CANDIDATE', 'CRITICAL'].includes(client.environment.assessment.status);
  return false;
}

function matchesSource(client: ClientEntry, filter: ClientSourceFilter) {
  if (filter === 'ALL') return true;
  if (filter === 'SCANNED') return client.scanned;
  if (filter === 'SAVED') return client.saved;
  if (filter === 'AD') return client.environment?.activeDirectory.exists === true;
  if (filter === 'KASPERSKY') return client.environment?.kaspersky.exists === true;
  if (filter === 'OPSI') return client.environment?.opsi.exists === true;
  return client.environment?.nessus.exists === true;
}

export function ClientsPage() {
  const navigate = useNavigate();
  const { savedTargets } = useTargets();
  const environment = useEnvironment();
  const [scannedHosts, setScannedHosts] = useState<StoredInventoryHost[]>([]);
  const [search, setSearch] = useState('');
  const [groupMode, setGroupMode] = useState<GroupMode>('none');
  const [statusFilter, setStatusFilter] = useState<ClientStatusFilter>('ALL');
  const [sourceFilter, setSourceFilter] = useState<ClientSourceFilter>('ALL');
  const [collapsed, setCollapsed] = useState<ReadonlySet<string>>(new Set());
  const [probes, setProbes] = useState<Record<string, HostProbe>>({});
  const [probing, setProbing] = useState(false);

  const reloadScanned = useCallback(() => invoke<ListInventoryHostsResult>('inventory', 'listHosts')
    .then((value) => setScannedHosts(value.hosts)).catch(() => setScannedHosts([])), []);
  useEffect(() => { void environment.ensureLoaded(); reloadScanned(); }, [environment.ensureLoaded, reloadScanned]);

  const savedClients = useMemo(() => savedTargets.filter((target) => target.role === 'Client'), [savedTargets]);
  const clients = useMemo(() => buildClientList(environment.result?.devices ?? [], scannedHosts, savedClients), [environment.result, scannedHosts, savedClients]);
  const filtered = useMemo(() => filterClients(clients, search)
    .filter((client) => matchesStatus(client, statusFilter) && matchesSource(client, sourceFilter)),
  [clients, search, sourceFilter, statusFilter]);
  const groups = useMemo(() => groupClients(filtered, groupMode), [filtered, groupMode]);

  const probeOnline = () => {
    const hosts = filtered.map((client) => client.host);
    if (!hosts.length) return;
    setProbing(true);
    invoke<ProbeHostsResult>('connectivity', 'probeHosts', { hosts }, 120_000)
      .then((value) => setProbes((current) => ({ ...current, ...Object.fromEntries(value.results.map((probe) => [probe.host.toUpperCase(), probe])) })))
      .catch(() => {}).finally(() => setProbing(false));
  };

  const columns: DataColumn<ClientEntry>[] = [
    { header: 'Device', cell: (client) => <div className="flex flex-col gap-1"><div className="font-medium text-slate-100">{client.name}</div>
      {client.os && <span className="text-xs text-slate-500">{client.os}</span>}
      <div className="flex flex-wrap gap-1">{probes[client.host.toUpperCase()] && <Badge tone={probes[client.host.toUpperCase()].reachable ? 'ok' : 'neutral'}>{probes[client.host.toUpperCase()].reachable ? 'Online' : 'Offline'}</Badge>}
        {client.scanned && <Badge tone="info">Scanned</Badge>}{client.saved && <Badge tone="accent">Saved</Badge>}</div></div>, sortValue: (client) => client.name },
    { header: 'AD', cell: (client) => <SourceBadge client={client} state={environment.result?.sources.activeDirectory ?? { availability: 'NOT_CONNECTED', error: null }} source="ad" /> },
    { header: 'Kaspersky', cell: (client) => <SourceBadge client={client} state={environment.result?.sources.kaspersky ?? { availability: 'NOT_CONNECTED', error: null }} source="ksc" /> },
    { header: 'opsi', cell: (client) => <SourceBadge client={client} state={environment.result?.sources.opsi ?? { availability: 'NOT_CONNECTED', error: null }} source="opsi" /> },
    { header: 'Nessus', cell: (client) => <SourceBadge client={client} state={environment.result?.sources.nessus ?? { availability: 'NOT_CONNECTED', error: null }} source="nessus" /> },
    { header: 'Overall', cell: (client) => { const value = overall(client); return <Badge tone={value.tone}>{value.label}</Badge>; } },
  ];

  const table = (rows: ClientEntry[]) => <DataTable columns={columns} rows={rows} getRowKey={(client) => client.key}
    onRowClick={(client) => navigate(`/clients/${encodeURIComponent(client.host)}`)} stickyHeader emptyMessage="No clients." />;

  return <div className="flex flex-col gap-4">
    <PageHeader title="Clients" subtitle="Central device inventory across AD, Kaspersky, opsi, Nessus and WEC scans">
      <div className="flex gap-2"><Button variant="secondary" onClick={() => navigate('/clients/compare')}>Compare</Button>
        <Button variant="secondary" onClick={probeOnline} disabled={probing || !filtered.length}>{probing ? 'Checking…' : 'Check online'}</Button>
        <Button variant="secondary" onClick={() => { void environment.refresh(); reloadScanned(); }} disabled={environment.loading}>{environment.loading ? 'Refreshing…' : 'Refresh'}</Button></div>
    </PageHeader>
    <Toolbar><Input type="search" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Filter by device, OS or finding" aria-label="Filter clients" className="w-64" />
      <Select fullWidth={false} value={statusFilter} onChange={(event) => setStatusFilter(event.target.value as ClientStatusFilter)} aria-label="Filter clients by status">
        <option value="ALL">All statuses</option><option value="HEALTHY">Healthy</option><option value="PROBLEMS">Problems</option><option value="INCOMPLETE">Incomplete</option><option value="UNMANAGED">Unmanaged</option>
      </Select>
      <Select fullWidth={false} value={sourceFilter} onChange={(event) => setSourceFilter(event.target.value as ClientSourceFilter)} aria-label="Filter clients by source">
        <option value="ALL">All sources</option><option value="AD">Active Directory</option><option value="KASPERSKY">Kaspersky</option><option value="OPSI">opsi</option><option value="NESSUS">Nessus</option><option value="SCANNED">Scanned</option><option value="SAVED">Saved</option>
      </Select>
      <Select fullWidth={false} value={groupMode} onChange={(event) => setGroupMode(event.target.value as GroupMode)} aria-label="Group clients by">
        <option value="none">No grouping</option><option value="os">Group by OS</option><option value="site">Group by site</option>
      </Select></Toolbar>
    <p className="text-sm text-slate-400">{filtered.length} devices · {filtered.filter((client) => client.scanned).length} scanned</p>
    {environment.error && <ErrorState title="Environment inventory failed" message={environment.error} />}
    {environment.loading && !environment.result ? <Spinner label="Loading environment inventory …" /> : !filtered.length ? <EmptyState title="No clients" message="No device matches the current filters." />
      : groupMode === 'none' ? table(filtered) : <div className="flex flex-col gap-3">{groups.map((group) => { const hidden = collapsed.has(group.label); return <div key={group.label} className="rounded-lg border border-slate-800">
        <button type="button" className="flex w-full gap-2 px-3 py-2 text-left text-sm text-slate-200" onClick={() => setCollapsed((current) => { const next = new Set(current); if (next.has(group.label)) next.delete(group.label); else next.add(group.label); return next; })}>{group.label} <span className="text-slate-500">{group.clients.length}</span></button>
        {!hidden && <div className="border-t border-slate-800">{table(group.clients)}</div>}</div>; })}</div>}
  </div>;
}
