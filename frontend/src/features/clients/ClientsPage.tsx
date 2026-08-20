import { useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import type {
  ClientWorkspaceListItem,
  ClientWorkspacePage,
  HygieneDevice,
  HygieneFindingCode,
  InventorySourceState,
  ProbeHostsResult,
} from '../../shared/api-types';
import { BridgeCancelledError, invoke, invokeCancellable, type CancellableBridgeInvocation } from '../../shared/bridge/bridgeClient';
import { presentError, type ErrorPresentation } from '../../shared/bridge/errorPresentation';
import { HygieneLoadStatus } from '../../shared/environment/HygieneLoadStatus';
import { useEnvironmentRequest } from '../../shared/environment/EnvironmentContext';
import { inventorySourceStatus } from '../../shared/environment/inventorySourceStatus';
import { useHygieneOperation } from '../../shared/environment/useHygieneOperation';
import { Badge } from '../../shared/ui/Badge';
import { Button } from '../../shared/ui/Button';
import { Card } from '../../shared/ui/Card';
import { DataTable, type DataColumn, type DataTableSort } from '../../shared/ui/DataTable';
import { Input } from '../../shared/ui/Input';
import { PageHeader } from '../../shared/ui/PageHeader';
import { Select } from '../../shared/ui/Select';
import { Spinner } from '../../shared/ui/Spinner';
import { EmptyState, ErrorState } from '../../shared/ui/States';
import { SummaryMetric } from '../../shared/ui/SummaryMetric';
import { SemanticStatusBadge, semanticStatusPresentation } from '../../shared/ui/SemanticStatusBadge';
import { Toolbar } from '../../shared/ui/Toolbar';
import { clientPostureFilters, clientPostureFilterFromUrl, clientPostureLabels, type ClientPostureFilter } from './clientPosture';
import {
  clientConnectivityStatus,
  ClientSemanticStatus,
  hygieneAssessmentStatus,
  sourceFreshnessStatus,
  sourcePresenceStatus,
  type ClientConnectivityState,
} from './clientStatus';

type ClientSourceFilter = 'ALL' | 'AD' | 'KASPERSKY' | 'OPSI' | 'NESSUS' | 'SCANNED' | 'SAVED';
type GroupMode = 'none' | 'os' | 'site';

function hasFinding(device: HygieneDevice, codes: HygieneFindingCode[]) {
  return device.assessment.findings.some((finding) => codes.includes(finding.code));
}

function EnvironmentSourceBadge({ name, state }: { name: string; state: InventorySourceState }) {
  const presentation = inventorySourceStatus(state.availability);
  const label = semanticStatusPresentation(presentation.status).label;
  return <span
    role="group"
    aria-label={`${name}: ${label}${presentation.context ? ` · ${presentation.context}` : ''}`}
    className="inline-flex items-center gap-1.5"
  >
    <span className="text-xs font-medium text-slate-400">{name}</span>
    <SemanticStatusBadge status={presentation.status} />
    {presentation.context && <span className="text-xs text-muted">{presentation.context}</span>}
  </span>;
}

function SourceBadge({ client, state, source }: { client: ClientWorkspaceListItem; state: InventorySourceState; source: 'ad' | 'ksc' | 'opsi' | 'nessus' }) {
  const device = client.environment;
  if (!device) return <ClientSemanticStatus status={{ dimension: 'availability', value: 'unknown' }} context="Not inventoried" />;
  const sourceExists = source === 'ad'
    ? device.activeDirectory.exists
    : source === 'ksc'
      ? device.kaspersky.exists
      : source === 'opsi'
        ? device.opsi.exists
        : device.nessus.exists;
  const sourceResultIncomplete = state.availability === 'PARTIAL' || state.availability === 'TRUNCATED';
  if (state.availability !== 'AVAILABLE' && (!sourceResultIncomplete || !sourceExists)) {
    const presentation = inventorySourceStatus(state.availability);
    return <ClientSemanticStatus {...presentation} />;
  }
  if (source === 'ad') {
    if (!device.activeDirectory.exists) return <ClientSemanticStatus status={sourcePresenceStatus(false, hasFinding(device, ['ORPHAN_KASPERSKY', 'ORPHAN_OPSI']))} />;
    if (device.activeDirectory.enabled === false) return <ClientSemanticStatus status={{ dimension: 'lifecycle', value: 'disabled' }} />;
    return <ClientSemanticStatus status={sourceFreshnessStatus(hasFinding(device, ['STALE_AD']), device.activeDirectory.lastLogonDate)} />;
  }
  if (source === 'ksc') {
    if (!device.kaspersky.exists) return <ClientSemanticStatus status={sourcePresenceStatus(false, hasFinding(device, ['MISSING_KASPERSKY']))} />;
    if (hasFinding(device, ['STALE_KASPERSKY'])) return <ClientSemanticStatus status={{ dimension: 'freshness', value: 'stale' }} />;
    if (hasFinding(device, ['OUTDATED_AGENT', 'OUTDATED_KES'])) return <ClientSemanticStatus status={{ dimension: 'lifecycle', value: 'update-available' }} />;
    return <ClientSemanticStatus status={sourceFreshnessStatus(false, device.kaspersky.lastSeen)} />;
  }
  if (source === 'opsi') {
    if (!device.opsi.exists) return <ClientSemanticStatus status={sourcePresenceStatus(false, hasFinding(device, ['MISSING_OPSI']))} />;
    return <ClientSemanticStatus status={sourceFreshnessStatus(hasFinding(device, ['STALE_OPSI']), device.opsi.lastSeen)} />;
  }
  if (!device.nessus.exists) return <ClientSemanticStatus status={sourcePresenceStatus(false, hasFinding(device, ['MISSING_NESSUS']))} />;
  if (hasFinding(device, ['NESSUS_CRITICAL_VULNERABILITIES'])) return <Badge tone="fail">Critical</Badge>;
  if (hasFinding(device, ['NESSUS_HIGH_VULNERABILITIES'])) return <Badge tone="warn">High</Badge>;
  return <ClientSemanticStatus status={sourceFreshnessStatus(hasFinding(device, ['STALE_NESSUS']), device.nessus.lastCompletedScanUtc)} />;
}

const unavailableSource: InventorySourceState = { availability: 'NOT_CONNECTED', error: null };

interface ClientProbeViewState {
  state: ClientConnectivityState;
  checkedAtUtc: string | null;
}

function ClientConnectivityStatus({ value }: { value: ClientProbeViewState }) {
  const presentation = clientConnectivityStatus(value.state);
  return <span className="inline-flex flex-col items-start gap-0.5">
    <ClientSemanticStatus {...presentation} />
    {value.checkedAtUtc && <span className="text-xs text-muted">Checked {new Date(value.checkedAtUtc).toLocaleString()}</span>}
  </span>;
}

export function ClientsPage() {
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const postureFilter = clientPostureFilterFromUrl(searchParams.get('posture'));
  const request = useEnvironmentRequest();
  const hygieneOperation = useHygieneOperation();
  const [workspace, setWorkspace] = useState<ClientWorkspacePage | null>(null);
  const [search, setSearch] = useState('');
  const [groupMode, setGroupMode] = useState<GroupMode>('none');
  const [sourceFilter, setSourceFilter] = useState<ClientSourceFilter>('ALL');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(50);
  const [sort, setSort] = useState<DataTableSort>({ column: 'device', direction: 'asc' });
  const [refreshRevision, setRefreshRevision] = useState(0);
  const lastForcedRevision = useRef(0);
  const requestId = useRef(0);
  const activeLoad = useRef<CancellableBridgeInvocation<ClientWorkspacePage> | null>(null);
  const [loading, setLoading] = useState(false);
  const [showEnvironmentProgress, setShowEnvironmentProgress] = useState(false);
  const [loadError, setLoadError] = useState<ErrorPresentation | null>(null);
  const [cancelled, setCancelled] = useState(false);
  const probeRequestId = useRef(0);
  const [probeStates, setProbeStates] = useState<Record<string, ClientProbeViewState>>({});
  const [probing, setProbing] = useState(false);
  const [probeError, setProbeError] = useState<ErrorPresentation | null>(null);

  useEffect(() => {
    const currentRequest = ++requestId.current;
    const force = refreshRevision > lastForcedRevision.current;
    lastForcedRevision.current = refreshRevision;
    const operationId = hygieneOperation.begin();
    setLoading(true);
    setShowEnvironmentProgress(workspace === null || force);
    setLoadError(null);
    setCancelled(false);
    const invocation = invokeCancellable<ClientWorkspacePage>('employeelifecycle', 'listClientWorkspace', {
      ...request,
      search,
      statusFilter: postureFilter,
      sourceFilter,
      groupMode,
      page,
      pageSize,
      sortColumn: sort.column,
      sortDirection: sort.direction,
      force,
      operationId,
    });
    activeLoad.current = invocation;
    void invocation.promise.then((value) => {
      if (requestId.current === currentRequest) setWorkspace(value);
    }).catch((caught) => {
      if (requestId.current === currentRequest && !(caught instanceof BridgeCancelledError)) {
        setLoadError(presentError(caught, { message: 'The client inventory could not be loaded.' }));
      }
    }).finally(() => {
      if (requestId.current === currentRequest) {
        activeLoad.current = null;
        hygieneOperation.end();
        setLoading(false);
        setShowEnvironmentProgress(false);
      }
    });
    return () => invocation.cancel();
  }, [groupMode, page, pageSize, postureFilter, refreshRevision, request, search, sort, sourceFilter, hygieneOperation.begin, hygieneOperation.end]);

  const probeOnline = () => {
    const hosts = workspace?.items.map((client) => client.host) ?? [];
    if (!hosts.length) return;
    const currentRequest = ++probeRequestId.current;
    const hostKeys = hosts.map((host) => host.toUpperCase());
    setProbeStates((current) => {
      const next = { ...current };
      for (const hostKey of hostKeys) next[hostKey] = { state: { kind: 'loading' }, checkedAtUtc: null };
      return next;
    });
    setProbeError(null);
    setProbing(true);
    void invoke<ProbeHostsResult>('connectivity', 'probeHosts', { hosts })
      .then((value) => {
        if (probeRequestId.current !== currentRequest) return;
        const checkedAtUtc = new Date().toISOString();
        const results = new Map(value.results.map((probe) => [probe.host.toUpperCase(), probe]));
        setProbeStates((current) => {
          const next = { ...current };
          for (const hostKey of hostKeys) {
            const probe = results.get(hostKey);
            next[hostKey] = {
              state: probe ? { kind: 'loaded', probe } : { kind: 'unavailable' },
              checkedAtUtc,
            };
          }
          return next;
        });
      })
      .catch((caught) => {
        if (probeRequestId.current !== currentRequest) return;
        const checkedAtUtc = new Date().toISOString();
        setProbeStates((current) => {
          const next = { ...current };
          for (const hostKey of hostKeys) next[hostKey] = { state: { kind: 'failed' }, checkedAtUtc };
          return next;
        });
        setProbeError(presentError(caught, {
          message: 'The visible clients could not be checked.',
          cause: 'The connectivity request did not complete.',
          action: 'Retry the connectivity check. Existing inventory data is unaffected.',
        }));
      })
      .finally(() => {
        if (probeRequestId.current === currentRequest) setProbing(false);
      });
  };

  const columns = useMemo<DataColumn<ClientWorkspaceListItem>[]>(() => {
    const sources = workspace?.sources;
    const deviceColumns: DataColumn<ClientWorkspaceListItem>[] = [
      { id: 'device', header: 'Device', sortable: true, cell: (client) => <div className="flex flex-col gap-1"><div className="font-medium text-slate-100">{client.name}</div>
        {client.os && <span className="text-xs text-muted">{client.os}</span>}
        {client.description && <span className="text-xs text-slate-400">{client.description}</span>}
        <div className="flex flex-wrap gap-1">{probeStates[client.host.toUpperCase()] && <ClientConnectivityStatus value={probeStates[client.host.toUpperCase()]} />}
          {client.scanned && <Badge tone="info">Scanned</Badge>}{client.saved && <Badge tone="accent">Saved</Badge>}</div></div> },
      { header: 'AD', cell: (client) => <SourceBadge client={client} state={sources?.activeDirectory ?? unavailableSource} source="ad" /> },
      { header: 'Kaspersky', cell: (client) => <SourceBadge client={client} state={sources?.kaspersky ?? unavailableSource} source="ksc" /> },
      { header: 'opsi', cell: (client) => <SourceBadge client={client} state={sources?.opsi ?? unavailableSource} source="opsi" /> },
      { header: 'Nessus', cell: (client) => <SourceBadge client={client} state={sources?.nessus ?? unavailableSource} source="nessus" /> },
      { id: 'overall', header: 'Overall', sortable: true, cell: (client) => <ClientSemanticStatus {...hygieneAssessmentStatus(client.environment?.assessment.status ?? null)} /> },
    ];
    if (groupMode === 'none') return deviceColumns;
    return [{
      header: groupMode === 'os' ? 'OS group' : 'Site group',
      cell: (client) => <span className="whitespace-nowrap text-slate-300">{client.groupLabel} <span className="text-muted">({client.groupTotal})</span></span>,
    }, ...deviceColumns];
  }, [groupMode, probeStates, workspace?.sources]);

  const resetPage = () => setPage(1);
  const applyPostureFilter = (value: ClientPostureFilter) => {
    const next = new URLSearchParams(searchParams);
    if (value === 'ALL') next.delete('posture');
    else next.set('posture', value);
    setSearchParams(next);
    resetPage();
  };
  const rows = workspace?.items ?? [];
  const retry = () => { setCancelled(false); setPage(1); setRefreshRevision((current) => current + 1); };

  return <div className="flex flex-col gap-4">
    <PageHeader title="Clients" subtitle="Canonical device inventory and fleet posture across AD, Kaspersky, opsi, Nessus and WEC scans">
      <div className="flex gap-2"><Button variant="secondary" onClick={() => navigate('/clients/compare')}>Compare</Button>
        <Button variant="secondary" onClick={probeOnline} disabled={probing || !rows.length}>{probing ? 'Checking…' : 'Check page connectivity'}</Button>
        <Button variant="secondary" onClick={() => { setPage(1); setRefreshRevision((current) => current + 1); }} disabled={loading}>{loading ? 'Refreshing…' : 'Refresh'}</Button></div>
    </PageHeader>
    {probeError && <ErrorState
      title="Client connectivity check failed"
      {...probeError}
      controls={<Button variant="secondary" onClick={probeOnline} disabled={probing || !rows.length}>Retry connectivity check</Button>}
    />}
    {workspace && <Card title="Fleet posture">
      <div className="mb-3 flex flex-wrap gap-2">
        <EnvironmentSourceBadge name="AD" state={workspace.sources.activeDirectory} />
        <EnvironmentSourceBadge name="Kaspersky" state={workspace.sources.kaspersky} />
        <EnvironmentSourceBadge name="opsi" state={workspace.sources.opsi} />
        <EnvironmentSourceBadge name="Nessus" state={workspace.sources.nessus} />
      </div>
      <div className="flex flex-wrap gap-3">
        <SummaryMetric label="Assessed devices" value={workspace.summary.total} onClick={() => applyPostureFilter('ALL')} active={postureFilter === 'ALL'} ariaLabel={`Show all clients (${workspace.summary.total})`} />
        <SummaryMetric label="Healthy" value={workspace.summary.healthy} tone="success" onClick={() => applyPostureFilter('HEALTHY')} active={postureFilter === 'HEALTHY'} ariaLabel={`Filter clients by Healthy (${workspace.summary.healthy})`} />
        <SummaryMetric label="Problems" value={workspace.summary.problems} tone={workspace.summary.problems ? 'warning' : 'success'} onClick={() => applyPostureFilter('PROBLEMS')} active={postureFilter === 'PROBLEMS'} ariaLabel={`Filter clients by Problems (${workspace.summary.problems})`} />
        <SummaryMetric label="Incomplete" value={workspace.summary.incomplete} onClick={() => applyPostureFilter('INCOMPLETE')} active={postureFilter === 'INCOMPLETE'} ariaLabel={`Filter clients by Incomplete (${workspace.summary.incomplete})`} />
        <SummaryMetric label="Stale" value={workspace.summary.stale} tone={workspace.summary.stale ? 'danger' : 'success'} onClick={() => applyPostureFilter('STALE')} active={postureFilter === 'STALE'} ariaLabel={`Filter clients by Stale (${workspace.summary.stale})`} />
        <SummaryMetric label="Missing Kaspersky" value={workspace.summary.missingKaspersky} tone={workspace.summary.missingKaspersky ? 'warning' : 'success'} onClick={() => applyPostureFilter('MISSING_KASPERSKY')} active={postureFilter === 'MISSING_KASPERSKY'} ariaLabel={`Filter clients by Missing Kaspersky (${workspace.summary.missingKaspersky})`} />
        <SummaryMetric label="Missing opsi" value={workspace.summary.missingOpsi} tone={workspace.summary.missingOpsi ? 'warning' : 'success'} onClick={() => applyPostureFilter('MISSING_OPSI')} active={postureFilter === 'MISSING_OPSI'} ariaLabel={`Filter clients by Missing opsi (${workspace.summary.missingOpsi})`} />
        <SummaryMetric label="Outdated" value={workspace.summary.outdated} tone={workspace.summary.outdated ? 'warning' : 'success'} onClick={() => applyPostureFilter('OUTDATED')} active={postureFilter === 'OUTDATED'} ariaLabel={`Filter clients by Outdated (${workspace.summary.outdated})`} />
        <SummaryMetric label="Missing Nessus" value={workspace.summary.missingNessus} tone={workspace.summary.missingNessus ? 'warning' : 'success'} onClick={() => applyPostureFilter('MISSING_NESSUS')} active={postureFilter === 'MISSING_NESSUS'} ariaLabel={`Filter clients by Missing Nessus (${workspace.summary.missingNessus})`} />
        <SummaryMetric label="Nessus Critical" value={workspace.summary.nessusCritical} tone={workspace.summary.nessusCritical ? 'danger' : 'success'} onClick={() => applyPostureFilter('NESSUS_CRITICAL')} active={postureFilter === 'NESSUS_CRITICAL'} ariaLabel={`Filter clients by Nessus Critical (${workspace.summary.nessusCritical})`} />
        <SummaryMetric label="Nessus High" value={workspace.summary.nessusHigh} tone={workspace.summary.nessusHigh ? 'warning' : 'success'} onClick={() => applyPostureFilter('NESSUS_HIGH')} active={postureFilter === 'NESSUS_HIGH'} ariaLabel={`Filter clients by Nessus High (${workspace.summary.nessusHigh})`} />
      </div>
      <p className="mt-3 text-xs text-muted">Assessed {new Date(workspace.assessedAtUtc).toLocaleString()}{workspace.domainName ? ` · AD domain ${workspace.domainName}` : ''}. Read-only posture; no remediation starts from this view.</p>
    </Card>}
    <Toolbar><Input type="search" value={search} onChange={(event) => { setSearch(event.target.value); resetPage(); }} placeholder="Filter by device, OS or finding" aria-label="Filter clients" className="w-64" />
      <Select fullWidth={false} value={postureFilter} onChange={(event) => applyPostureFilter(event.target.value as ClientPostureFilter)} aria-label="Filter clients by posture">
        {clientPostureFilters.map((value) => <option key={value} value={value}>{clientPostureLabels[value]}</option>)}
      </Select>
      <Select fullWidth={false} value={sourceFilter} onChange={(event) => { setSourceFilter(event.target.value as ClientSourceFilter); resetPage(); }} aria-label="Filter clients by source">
        <option value="ALL">All sources</option><option value="AD">Active Directory</option><option value="KASPERSKY">Kaspersky</option><option value="OPSI">opsi</option><option value="NESSUS">Nessus</option><option value="SCANNED">Scanned</option><option value="SAVED">Saved</option>
      </Select>
      <Select fullWidth={false} value={groupMode} onChange={(event) => { setGroupMode(event.target.value as GroupMode); resetPage(); }} aria-label="Group clients by">
        <option value="none">No grouping</option><option value="os">Group by OS</option><option value="site">Group by site</option>
      </Select></Toolbar>
    <p className="text-sm text-slate-400">{workspace?.total ?? 0} devices · {workspace?.scannedTotal ?? 0} scanned{workspace && workspace.total !== workspace.snapshotTotal ? ` · ${workspace.snapshotTotal} total` : ''}</p>
    {loading && showEnvironmentProgress && <HygieneLoadStatus progress={hygieneOperation.progress} elapsedSeconds={hygieneOperation.elapsedSeconds} onCancel={() => { setCancelled(true); activeLoad.current?.cancel(); }} />}
    {cancelled && !loading && <div className="flex items-center gap-3 rounded-lg border border-slate-800 p-4"><p className="text-sm text-slate-300">Environment load cancelled. The previous successful data remains unchanged.</p><Button variant="secondary" onClick={retry}>Retry</Button></div>}
    {loadError && <ErrorState
      title="Environment inventory failed"
      {...loadError}
      controls={<Button variant="secondary" onClick={retry}>Retry environment load</Button>}
    />}
    {loading && !workspace && !showEnvironmentProgress ? <Spinner label="Loading environment inventory …" /> : !rows.length && !loading ? <EmptyState title="No clients" message="No device matches the current filters." />
      : <DataTable columns={columns} rows={rows} getRowKey={(client) => client.key}
        onRowClick={(client) => navigate(`/clients/${encodeURIComponent(client.host)}`)} stickyHeader emptyMessage="No clients."
        loading={loading} sort={sort} onSortChange={(value) => { setSort(value); resetPage(); }}
        pagination={{ page: workspace?.page ?? page, pageSize: workspace?.pageSize ?? pageSize, total: workspace?.total ?? 0, onPageChange: setPage, onPageSizeChange: (value) => { setPageSize(value); resetPage(); } }} />}
  </div>;
}
