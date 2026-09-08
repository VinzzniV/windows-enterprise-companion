import { useEffect, useMemo, useRef, useState } from 'react';
import { useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import type {
  AppInfoResponse,
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
import { useEnvironment, useEnvironmentRequest } from '../../shared/environment/EnvironmentContext';
import { inventorySourceStatus } from '../../shared/environment/inventorySourceStatus';
import { useHygieneOperation } from '../../shared/environment/useHygieneOperation';
import { Badge } from '../../shared/ui/Badge';
import { Button } from '../../shared/ui/Button';
import { DataTable, type DataColumn, type DataTableGroup, type DataTableSort } from '../../shared/ui/DataTable';
import { DetailsDisclosure } from '../../shared/ui/DetailsDisclosure';
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
import { ClientBulkActions } from './ClientBulkActions';
import { clientListScope } from './clientListNavigation';

type ClientSourceFilter = 'ALL' | 'AD' | 'KASPERSKY' | 'OPSI' | 'NESSUS' | 'SCANNED' | 'SAVED';
type GroupMode = 'none' | 'os' | 'site';

const sourceFilters: readonly ClientSourceFilter[] = ['ALL', 'AD', 'KASPERSKY', 'OPSI', 'NESSUS', 'SCANNED', 'SAVED'];
const groupModes: readonly GroupMode[] = ['none', 'os', 'site'];

function sourceFilterFromUrl(value: string | null): ClientSourceFilter {
  return sourceFilters.includes(value as ClientSourceFilter) ? value as ClientSourceFilter : 'ALL';
}

function groupModeFromUrl(value: string | null): GroupMode {
  return groupModes.includes(value as GroupMode) ? value as GroupMode : 'none';
}

function positiveIntegerFromUrl(value: string | null, fallback: number): number {
  const parsed = Number(value);
  return Number.isInteger(parsed) && parsed > 0 ? parsed : fallback;
}

function pageSizeFromUrl(value: string | null): number {
  const parsed = positiveIntegerFromUrl(value, 50);
  return [25, 50, 100].includes(parsed) ? parsed : 50;
}

function sortFromUrl(column: string | null, direction: string | null): DataTableSort {
  return {
    column: column === 'overall' ? 'overall' : 'device',
    direction: direction === 'desc' ? 'desc' : 'asc',
  };
}

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
    const missingAgent = hasFinding(device, ['MISSING_KASPERSKY_AGENT']);
    const missingKes = hasFinding(device, ['MISSING_KES']);
    if (missingAgent || missingKes) {
      const missingComponents = [missingAgent && 'Network Agent', missingKes && 'KES'].filter(Boolean).join(' + ');
      return <ClientSemanticStatus status={sourcePresenceStatus(false, true)} context={`${missingComponents} not installed`} />;
    }
    if (hasFinding(device, ['STALE_KASPERSKY'])) return <ClientSemanticStatus status={{ dimension: 'freshness', value: 'stale' }} />;
    if (hasFinding(device, ['OUTDATED_AGENT', 'OUTDATED_KES'])) return <ClientSemanticStatus status={{ dimension: 'lifecycle', value: 'update-available' }} />;
    return <ClientSemanticStatus status={sourceFreshnessStatus(false, device.kaspersky.lastSeen)} />;
  }
  if (source === 'opsi') {
    if (!device.opsi.exists) return <ClientSemanticStatus status={sourcePresenceStatus(false, hasFinding(device, ['MISSING_OPSI']))} />;
    return <ClientSemanticStatus status={sourceFreshnessStatus(hasFinding(device, ['STALE_OPSI']), device.opsi.lastSeen)} />;
  }
  if (!device.nessus.exists || !device.nessus.lastCompletedScanUtc) {
    return <ClientSemanticStatus status={sourcePresenceStatus(false, hasFinding(device, ['MISSING_NESSUS']))} context={device.nessus.exists ? 'No completed scan' : null} />;
  }
  const coverage = sourceResultIncomplete ? <Badge tone="warn">Partial coverage</Badge> : null;
  const observed = <span className="text-xs text-muted">Scan {new Date(device.nessus.lastCompletedScanUtc).toLocaleString()}</span>;
  if (hasFinding(device, ['NESSUS_CRITICAL_VULNERABILITIES'])) return <span className="flex flex-col items-start gap-1">
    <span className="flex flex-wrap gap-1"><Badge tone="fail">Critical · {device.nessus.critical} instances</Badge>{coverage}</span>
    {observed}
  </span>;
  if (hasFinding(device, ['NESSUS_HIGH_VULNERABILITIES'])) return <span className="flex flex-col items-start gap-1">
    <span className="flex flex-wrap gap-1"><Badge tone="warn">High · {device.nessus.high} instances</Badge>{coverage}</span>
    {observed}
  </span>;
  return <ClientSemanticStatus status={sourceFreshnessStatus(hasFinding(device, ['STALE_NESSUS']), device.nessus.lastCompletedScanUtc)} />;
}

const unavailableSource: InventorySourceState = { availability: 'NOT_CONNECTED', error: null };

interface ClientProbeViewState {
  state: ClientConnectivityState;
  checkedAtUtc: string | null;
}

interface ClientsViewState {
  probeStates: Record<string, ClientProbeViewState>;
  selectedHosts: string[];
  scrollTop: number;
  returnTo: string;
  scope: string;
}

function ClientConnectivityStatus({ value }: { value: ClientProbeViewState }) {
  const presentation = clientConnectivityStatus(value.state);
  return <span className="inline-flex flex-col items-start gap-0.5">
    <ClientSemanticStatus {...presentation} />
  </span>;
}

function SourceLastSeen({ source, value }: { source: string; value: string | null }) {
  if (!value) return null;
  return <span className="text-xs text-muted">{source} Last seen {new Date(value).toLocaleString()}</span>;
}

export function ClientsPage() {
  const environment = useEnvironment();
  const invalidateEnvironment = environment.invalidate;
  const environmentRefreshRevision = environment.refreshRevision;
  const lastEnvironmentRefreshRevision = useRef(0);
  const navigate = useNavigate();
  const location = useLocation();
  const [searchParams, setSearchParams] = useSearchParams();
  const restoredView = location.state as ClientsViewState | null;
  const initialView = useRef(restoredView);
  const postureFilter = clientPostureFilterFromUrl(searchParams.get('posture'));
  const request = useEnvironmentRequest();
  const scope = clientListScope(request);
  const hygieneOperation = useHygieneOperation();
  const [workspace, setWorkspace] = useState<ClientWorkspacePage | null>(null);
  const search = searchParams.get('q') ?? '';
  const groupMode = groupModeFromUrl(searchParams.get('group'));
  const sourceFilter = sourceFilterFromUrl(searchParams.get('source'));
  const page = positiveIntegerFromUrl(searchParams.get('page'), 1);
  const pageSize = pageSizeFromUrl(searchParams.get('pageSize'));
  const sortColumn = searchParams.get('sort');
  const sortDirection = searchParams.get('direction');
  const sort = useMemo(() => sortFromUrl(sortColumn, sortDirection), [sortColumn, sortDirection]);
  const [appliedSearch, setAppliedSearch] = useState(search);
  const [refreshRevision, setRefreshRevision] = useState(0);
  const lastForcedRevision = useRef(0);
  const requestId = useRef(0);
  const activeLoad = useRef<CancellableBridgeInvocation<ClientWorkspacePage> | null>(null);
  const [loading, setLoading] = useState(false);
  const [showEnvironmentProgress, setShowEnvironmentProgress] = useState(false);
  const [loadError, setLoadError] = useState<ErrorPresentation | null>(null);
  const [cancelled, setCancelled] = useState(false);
  const probeRequestId = useRef(0);
  const [probeStates, setProbeStates] = useState<Record<string, ClientProbeViewState>>(() => initialView.current?.probeStates ?? {});
  const [probing, setProbing] = useState(false);
  const [probeError, setProbeError] = useState<ErrorPresentation | null>(null);
  const [selectedHosts, setSelectedHosts] = useState<string[]>(() => initialView.current?.selectedHosts ?? []);
  const [maxBatchHosts, setMaxBatchHosts] = useState<number | null>(null);
  const [batchRunning, setBatchRunning] = useState(false);
  const pageRoot = useRef<HTMLDivElement>(null);
  const restoredScrollTop = useRef(initialView.current?.scope === scope ? initialView.current.scrollTop : null);
  const scrollRestored = useRef(false);

  useEffect(() => {
    const timer = window.setTimeout(() => setAppliedSearch(search), 250);
    return () => window.clearTimeout(timer);
  }, [search]);

  useEffect(() => {
    let active = true;
    void invoke<AppInfoResponse>('system', 'getAppInfo')
      .then((appInfo) => {
        if (active) setMaxBatchHosts(appInfo.maxBatchHosts);
      })
      .catch(() => {
        if (active) setMaxBatchHosts(null);
      });
    return () => { active = false; };
  }, []);

  useEffect(() => {
    const currentRequest = ++requestId.current;
    const force = environmentRefreshRevision > lastEnvironmentRefreshRevision.current
      || refreshRevision > lastForcedRevision.current;
    const operationId = hygieneOperation.begin();
    setLoading(true);
    setShowEnvironmentProgress(workspace === null || force);
    setLoadError(null);
    setCancelled(false);
    const invocation = invokeCancellable<ClientWorkspacePage>('employeelifecycle', 'listClientWorkspace', {
      ...request,
      search: appliedSearch,
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
      if (requestId.current === currentRequest) {
        if (force) {
          lastEnvironmentRefreshRevision.current = environmentRefreshRevision;
          lastForcedRevision.current = refreshRevision;
        }
        setWorkspace(value);
        if (force) invalidateEnvironment(false);
        if (!scrollRestored.current && restoredScrollTop.current !== null) {
          scrollRestored.current = true;
          requestAnimationFrame(() => {
            const scrollContainer = pageRoot.current?.closest<HTMLElement>('[data-scroll-container="application"]');
            if (scrollContainer) scrollContainer.scrollTop = restoredScrollTop.current ?? 0;
          });
        }
      }
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
  }, [appliedSearch, environmentRefreshRevision, groupMode, page, pageSize, postureFilter, refreshRevision, request, sort, sourceFilter, hygieneOperation.begin, hygieneOperation.end, invalidateEnvironment]);

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
      { id: 'select', header: 'Select', className: 'w-14', cell: (client) => {
        const key = client.host.toUpperCase();
        const checked = selectedHosts.some((host) => host.toUpperCase() === key);
        const limitReached = maxBatchHosts !== null && selectedHosts.length >= maxBatchHosts;
        return <input
          type="checkbox"
          aria-label={`Select ${client.name} for bulk scan`}
          checked={checked}
          disabled={batchRunning || maxBatchHosts === null || (!checked && limitReached)}
          onClick={(event) => event.stopPropagation()}
          onChange={() => setSelectedHosts((current) => {
            const existing = current.some((host) => host.toUpperCase() === key);
            if (existing) return current.filter((host) => host.toUpperCase() !== key);
            if (maxBatchHosts === null || current.length >= maxBatchHosts) return current;
            return [...current, client.host];
          })}
          className="h-4 w-4 cursor-pointer accent-accent-500 disabled:cursor-not-allowed"
        />;
      } },
      { id: 'device', header: 'Device', sortable: true, className: 'w-72', cell: (client) => <div className="flex flex-col gap-1"><div className="font-medium text-slate-100">{client.name}</div>
        {client.os && <span className="text-xs text-muted">{client.os}</span>}
        {client.description && <span className="line-clamp-1 text-xs text-slate-400" title={client.description}>{client.description}</span>}
        <div className="flex flex-col items-start gap-1">
          <div className="flex flex-wrap items-start gap-x-2 gap-y-0.5">
            <SourceLastSeen source="AD" value={client.environment?.activeDirectory.lastLogonDate ?? null} />
            <SourceLastSeen source="Kaspersky" value={client.environment?.kaspersky.lastSeen ?? null} />
            <SourceLastSeen source="opsi" value={client.environment?.opsi.lastSeen ?? null} />
          </div>
          <div className="flex flex-wrap gap-1">
            {probeStates[client.host.toUpperCase()] && <ClientConnectivityStatus value={probeStates[client.host.toUpperCase()]} />}
            {client.scanned && <Badge tone="info">Scanned</Badge>}{client.saved && <Badge tone="accent">Saved</Badge>}
          </div>
        </div></div> },
      { id: 'overall', header: 'Overall', sortable: true, className: 'w-36', cell: (client) => <ClientSemanticStatus {...hygieneAssessmentStatus(client.environment?.assessment.status ?? null)} /> },
      { header: 'AD', className: 'w-28', cell: (client) => <SourceBadge client={client} state={sources?.activeDirectory ?? unavailableSource} source="ad" /> },
      { header: 'Kaspersky', className: 'w-32', cell: (client) => <SourceBadge client={client} state={sources?.kaspersky ?? unavailableSource} source="ksc" /> },
      { header: 'opsi', className: 'w-28', cell: (client) => <SourceBadge client={client} state={sources?.opsi ?? unavailableSource} source="opsi" /> },
      { header: 'Nessus', className: 'w-28', cell: (client) => <SourceBadge client={client} state={sources?.nessus ?? unavailableSource} source="nessus" /> },
    ];
    return deviceColumns;
  }, [batchRunning, maxBatchHosts, probeStates, selectedHosts, workspace?.sources]);

  const updateListUrl = (updates: Record<string, string | null>, resetPage = false) => {
    const next = new URLSearchParams(searchParams);
    for (const [key, value] of Object.entries(updates)) {
      if (value === null || value === '') next.delete(key);
      else next.set(key, value);
    }
    if (resetPage) next.delete('page');
    setSearchParams(next, { replace: true });
  };
  const applyPostureFilter = (value: ClientPostureFilter) => {
    updateListUrl({ posture: value === 'ALL' ? null : value }, true);
  };
  const rows = workspace?.items ?? [];
  const coverageComplete = workspace ? Object.values(workspace.sources).every((source) => source.availability === 'AVAILABLE') : false;
  const zeroKnownTone = (count: number, problemTone: 'warning' | 'danger') => count > 0
    ? problemTone
    : coverageComplete
      ? 'success'
      : 'neutral';
  const rowGroup = groupMode === 'none' ? undefined : (client: ClientWorkspaceListItem): DataTableGroup => {
    const label = client.groupLabel ?? (groupMode === 'os' ? 'Unknown OS' : 'Other');
    return {
      key: label,
      label: <>{label} <span className="text-muted">({client.groupTotal ?? 0})</span></>,
    };
  };
  const openClient = (client: ClientWorkspaceListItem) => {
    const returnTo = `${location.pathname}${location.search}`;
    const scrollContainer = pageRoot.current?.closest<HTMLElement>('[data-scroll-container="application"]');
    const viewState: ClientsViewState = {
      probeStates,
      selectedHosts,
      scrollTop: scrollContainer?.scrollTop ?? 0,
      returnTo,
      scope,
    };
    navigate(returnTo, { replace: true, state: viewState });
    navigate(`/clients/${encodeURIComponent(client.host)}`, { state: viewState });
  };
  const retry = () => { setCancelled(false); setAppliedSearch(search); updateListUrl({}, true); setRefreshRevision((current) => current + 1); };
  const selectVisibleHosts = () => {
    if (maxBatchHosts === null || batchRunning) return;
    setSelectedHosts((current) => {
      const next = [...current];
      const selectedKeys = new Set(current.map((host) => host.toUpperCase()));
      for (const client of rows) {
        const key = client.host.toUpperCase();
        if (!selectedKeys.has(key) && next.length < maxBatchHosts) {
          next.push(client.host);
          selectedKeys.add(key);
        }
      }
      return next;
    });
  };

  return <div ref={pageRoot} className="flex flex-col gap-4">
    <PageHeader title="Clients" subtitle="Combined device list and status across Active Directory, Kaspersky, opsi, Nessus and saved WEC scans">
      <div className="flex gap-2"><Button variant="secondary" onClick={() => navigate('/clients/compare')}>Compare</Button>
        <Button variant="secondary" onClick={probeOnline} disabled={probing || !rows.length}>{probing ? 'Checking…' : 'Check page connectivity'}</Button>
        <Button variant="secondary" onClick={() => { setAppliedSearch(search); updateListUrl({}, true); setRefreshRevision((current) => current + 1); }} disabled={loading}>{loading ? 'Refreshing…' : 'Refresh'}</Button></div>
    </PageHeader>
    {probeError && <ErrorState
      title="Client connectivity check failed"
      {...probeError}
      controls={<Button variant="secondary" onClick={probeOnline} disabled={probing || !rows.length}>Retry connectivity check</Button>}
    />}
    {workspace && <section className="rounded-lg border border-slate-800 bg-slate-900/70 px-3 py-2.5" aria-labelledby="fleet-posture-heading">
      <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
        <h2 id="fleet-posture-heading" className="text-sm font-semibold text-slate-200">Device status</h2>
        <EnvironmentSourceBadge name="AD" state={workspace.sources.activeDirectory} />
        <EnvironmentSourceBadge name="Kaspersky" state={workspace.sources.kaspersky} />
        <EnvironmentSourceBadge name="opsi" state={workspace.sources.opsi} />
        <EnvironmentSourceBadge name="Nessus" state={workspace.sources.nessus} />
        <span className="ml-auto text-xs text-muted">Snapshot {workspace.snapshotRevision} · {new Date(workspace.assessedAtUtc).toLocaleString()}</span>
      </div>
      {!coverageComplete && <p className="mt-2 text-xs text-warn-300" role="status">
        Counts are known results only; one or more sources have incomplete coverage.
      </p>}
      <p className="mt-2 text-xs text-slate-400" title="Stored-only and saved-only clients remain in the table but are excluded from status counts.">
        Status counts cover unique devices in the current AD, Kaspersky, opsi and Nessus assessment before table filters.
        Stored-Inventory-only and saved-target-only devices are excluded from these counters.
      </p>
      <div className="mt-2">
        <DetailsDisclosure summary={`${workspace.summary.total} evaluated · ${workspace.summary.problems} known problem devices · ${workspace.summary.incomplete} with incomplete coverage — show status filters`}>
          <div className="grid grid-cols-[repeat(auto-fit,minmax(10rem,1fr))] gap-2">
        <SummaryMetric label="Evaluated devices" value={workspace.summary.total} onClick={() => applyPostureFilter('ALL')} active={postureFilter === 'ALL'} ariaLabel={`Show all clients (${workspace.summary.total})`} />
        <SummaryMetric label="Healthy" value={workspace.summary.healthy} tone="success" onClick={() => applyPostureFilter('HEALTHY')} active={postureFilter === 'HEALTHY'} ariaLabel={`Filter clients by Healthy (${workspace.summary.healthy})`} />
        <SummaryMetric label="Known problem devices" value={workspace.summary.problems} tone={zeroKnownTone(workspace.summary.problems, 'warning')} onClick={() => applyPostureFilter('PROBLEMS')} active={postureFilter === 'PROBLEMS'} ariaLabel={`Filter clients by Problems (${workspace.summary.problems})`} />
        <SummaryMetric label="Devices with incomplete coverage" value={workspace.summary.incomplete} tone={workspace.summary.incomplete ? 'warning' : 'success'} onClick={() => applyPostureFilter('INCOMPLETE')} active={postureFilter === 'INCOMPLETE'} ariaLabel={`Filter clients by Incomplete (${workspace.summary.incomplete})`} />
        <SummaryMetric label="Known stale devices" value={workspace.summary.stale} tone={zeroKnownTone(workspace.summary.stale, 'danger')} onClick={() => applyPostureFilter('STALE')} active={postureFilter === 'STALE'} ariaLabel={`Filter clients by Stale (${workspace.summary.stale})`} />
        <SummaryMetric label="Known missing Kaspersky devices" value={workspace.summary.missingKaspersky} tone={zeroKnownTone(workspace.summary.missingKaspersky, 'warning')} onClick={() => applyPostureFilter('MISSING_KASPERSKY')} active={postureFilter === 'MISSING_KASPERSKY'} ariaLabel={`Filter clients by Missing Kaspersky (${workspace.summary.missingKaspersky})`} />
        <SummaryMetric label="Known missing opsi devices" value={workspace.summary.missingOpsi} tone={zeroKnownTone(workspace.summary.missingOpsi, 'warning')} onClick={() => applyPostureFilter('MISSING_OPSI')} active={postureFilter === 'MISSING_OPSI'} ariaLabel={`Filter clients by Missing opsi (${workspace.summary.missingOpsi})`} />
        <SummaryMetric label="Known outdated devices" value={workspace.summary.outdated} tone={zeroKnownTone(workspace.summary.outdated, 'warning')} onClick={() => applyPostureFilter('OUTDATED')} active={postureFilter === 'OUTDATED'} ariaLabel={`Filter clients by Outdated (${workspace.summary.outdated})`} />
        <SummaryMetric label="Known missing Nessus devices" value={workspace.summary.missingNessus} tone={zeroKnownTone(workspace.summary.missingNessus, 'warning')} onClick={() => applyPostureFilter('MISSING_NESSUS')} active={postureFilter === 'MISSING_NESSUS'} ariaLabel={`Filter clients by Missing Nessus (${workspace.summary.missingNessus})`} />
        <SummaryMetric label="Devices with known Nessus Critical" value={workspace.summary.nessusCritical} tone={zeroKnownTone(workspace.summary.nessusCritical, 'danger')} onClick={() => applyPostureFilter('NESSUS_CRITICAL')} active={postureFilter === 'NESSUS_CRITICAL'} ariaLabel={`Filter clients by Nessus Critical (${workspace.summary.nessusCritical})`} />
        <SummaryMetric label="Devices with known Nessus High" value={workspace.summary.nessusHigh} tone={zeroKnownTone(workspace.summary.nessusHigh, 'warning')} onClick={() => applyPostureFilter('NESSUS_HIGH')} active={postureFilter === 'NESSUS_HIGH'} ariaLabel={`Filter clients by Nessus High (${workspace.summary.nessusHigh})`} />
          </div>
        </DetailsDisclosure>
      </div>
      <p className="mt-2 text-xs text-muted">{workspace.domainName ? `AD domain ${workspace.domainName} · ` : ''}Read-only status; no remediation starts from this view.</p>
    </section>}
    <Toolbar actions={<>
      <span className="text-xs tabular-nums text-muted">{selectedHosts.length}/{maxBatchHosts ?? '—'} selected</span>
      <Button variant="ghost" onClick={selectVisibleHosts} disabled={batchRunning || maxBatchHosts === null || !rows.length || selectedHosts.length >= maxBatchHosts}>Select page</Button>
      <Button variant="ghost" onClick={() => setSelectedHosts([])} disabled={batchRunning || !selectedHosts.length}>Clear selection</Button>
    </>}><Input type="search" value={search} onChange={(event) => updateListUrl({ q: event.target.value }, true)} placeholder="Filter by device, OS or finding" aria-label="Filter clients" className="w-64" />
      <Select fullWidth={false} value={postureFilter} onChange={(event) => applyPostureFilter(event.target.value as ClientPostureFilter)} aria-label="Filter clients by status">
        {clientPostureFilters.map((value) => <option key={value} value={value}>{clientPostureLabels[value]}</option>)}
      </Select>
      <Select fullWidth={false} value={sourceFilter} onChange={(event) => updateListUrl({ source: event.target.value === 'ALL' ? null : event.target.value }, true)} aria-label="Filter clients by source">
        <option value="ALL">All sources</option><option value="AD">Active Directory</option><option value="KASPERSKY">Kaspersky</option><option value="OPSI">opsi</option><option value="NESSUS">Nessus</option><option value="SCANNED">Scanned</option><option value="SAVED">Saved</option>
      </Select>
      <Select fullWidth={false} value={groupMode} onChange={(event) => updateListUrl({ group: event.target.value === 'none' ? null : event.target.value }, true)} aria-label="Group clients by">
        <option value="none">No grouping</option><option value="os">Group by OS</option><option value="site">Group by site</option>
      </Select>
      <Button variant="ghost" onClick={() => setSearchParams(new URLSearchParams(), { replace: true })} disabled={!searchParams.size}>Reset filters</Button>
    </Toolbar>
    <p className="text-xs text-muted">Search and list filters apply immediately; Reset filters restores the default list.</p>
    {selectedHosts.length > 0 && <ClientBulkActions
      selectedHosts={selectedHosts}
      maxBatchHosts={maxBatchHosts}
      onRunningChange={setBatchRunning}
      onCompleted={() => setRefreshRevision((current) => current + 1)}
    />}
    <p className="text-sm text-slate-400">
      {workspace?.total ?? 0} matching devices · {workspace?.scannedTotal ?? 0} with stored Inventory · {workspace?.snapshotTotal ?? 0} merged candidates before table filters
    </p>
    {loading && showEnvironmentProgress && <HygieneLoadStatus progress={hygieneOperation.progress} elapsedSeconds={hygieneOperation.elapsedSeconds} onCancel={() => { setCancelled(true); activeLoad.current?.cancel(); }} />}
    {cancelled && !loading && <div className="flex items-center gap-3 rounded-lg border border-slate-800 p-4"><p className="text-sm text-slate-300">Environment load cancelled. The previous successful data remains unchanged.</p><Button variant="secondary" onClick={retry}>Retry</Button></div>}
    {loadError && <ErrorState
      title="Environment inventory failed"
      {...loadError}
      controls={<Button variant="secondary" onClick={retry}>Retry environment load</Button>}
    />}
    {loading && !workspace && !showEnvironmentProgress ? <Spinner label="Loading environment inventory …" /> : !rows.length && !loading ? <EmptyState title="No clients" message="No device matches the current filters." />
      : <DataTable layout="fixed" columns={columns} rows={rows} getRowKey={(client) => client.key} groupBy={rowGroup}
        onRowClick={batchRunning ? undefined : openClient} stickyHeader emptyMessage="No clients."
        loading={loading} sort={sort} onSortChange={(value) => updateListUrl({ sort: value.column === 'device' ? null : value.column, direction: value.direction === 'asc' ? null : value.direction }, true)}
        pagination={{ page: workspace?.page ?? page, pageSize: workspace?.pageSize ?? pageSize, total: workspace?.groupCount ?? workspace?.total ?? 0, itemLabel: groupMode === 'none' ? undefined : 'groups', onPageChange: (value) => updateListUrl({ page: value === 1 ? null : String(value) }), onPageSizeChange: (value) => updateListUrl({ pageSize: value === 50 ? null : String(value) }, true) }} />}
  </div>;
}
