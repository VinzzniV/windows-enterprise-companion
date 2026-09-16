import { useLayoutEffect, useMemo, useRef } from 'react';
import { Link, useLocation, useSearchParams } from 'react-router-dom';
import type { ObjectKind } from '../api-types.generated';
import { objectPath, objectSourceLabel } from './objectRoutes';
import { useWorkingSet } from './WorkingSetContext';
import { queryWorkingSet, type WorkingSetFilter, type WorkingSetObject, type WorkingSetSource } from './workingSet';
import { workingSetSourceLabel } from './workingSetSources';
import { managementRecordPath } from './managementRecordRoutes';
import { WorkingSetCoverage } from './WorkingSetCoverage';
import { WorkingSetSourceControls } from './WorkingSetSourceControls';
import { DataTable, type DataColumn } from '../ui/DataTable';
import { Input } from '../ui/Input';
import { Select } from '../ui/Select';
import { PageHeader } from '../ui/PageHeader';
import { workingSetEntryState, workingSetRestoreKey } from './workingSetNavigation';

const positions = new Map<string, { scroll: number; focus: string | null }>();
const sources: WorkingSetSource[] = ['ACTIVE_DIRECTORY', 'ENTRA', 'INTUNE', 'WEC', 'KASPERSKY', 'OPSI', 'NESSUS'];
const titles: Record<ObjectKind, string> = { DEVICE: 'Devices', USER: 'Users', GROUP: 'Groups' };
function compactAge(value: string | null) {
  if (!value) return null;
  const minutes = Math.max(0, Math.floor((Date.now() - Date.parse(value)) / 60_000));
  return minutes < 60 ? `${minutes}m old` : minutes < 1_440 ? `${Math.floor(minutes / 60)}h old` : `${Math.floor(minutes / 1_440)}d old`;
}

export function ObjectWorkingSetPage({ kind }: { kind: ObjectKind }) {
  const workspace = useWorkingSet();
  const location = useLocation();
  const [parameters, setParameters] = useSearchParams();
  const root = useRef<HTMLDivElement>(null);
  const restored = useRef(false);
  const query = parameters.get('q') ?? '';
  const directoryDiscovery = parameters.get('discover') === 'ad';
  const source = sources.find(value => value === parameters.get('source'));
  const accountState = ['enabled', 'disabled', 'unknown'].find(value => value === parameters.get('account')) as 'enabled' | 'disabled' | 'unknown' | undefined;
  const operatingSystem = parameters.get('os') ?? '';
  const skuId = parameters.get('sku') ?? '';
  const adAccountState = ['enabled', 'disabled', 'unknown'].find(value => value === parameters.get('adAccount')) as WorkingSetFilter['accountState'];
  const entraAccountState = ['enabled', 'disabled', 'unknown'].find(value => value === parameters.get('entraAccount')) as WorkingSetFilter['accountState'];
  const intuneCompliance = parameters.get('compliance') ?? '';
  const intuneManagement = parameters.get('management') ?? '';
  const identityState = ['unresolved', 'candidates', 'conflict'].find(value => value === parameters.get('identity')) as WorkingSetFilter['identityState'];
  const storedEvidence = ['saved', 'scanned'].find(value => value === parameters.get('stored')) as WorkingSetFilter['storedEvidence'];
  const requestedTenant = parameters.get('tenant');
  const tenantMismatch = Boolean(requestedTenant && requestedTenant.toLowerCase() !== workspace.policy?.tenantId?.toLowerCase());
  const descending = parameters.get('sort') === 'desc';
  const pageSize = [25, 50, 100].find(value => value === Number(parameters.get('size'))) ?? 25;
  const result = useMemo(() => workspace.displayed && !tenantMismatch ? queryWorkingSet(workspace.displayed, { kind, query, source, accountState,
    operatingSystem, skuId, adAccountState, entraAccountState, intuneCompliance, intuneManagement, identityState, storedEvidence,
    descending, page: Number(parameters.get('page')) || 1, pageSize }) : null,
  [workspace.displayed, tenantMismatch, kind, query, source, accountState, operatingSystem, skuId, adAccountState, entraAccountState,
    intuneCompliance, intuneManagement, identityState, storedEvidence, descending, parameters, pageSize]);
  const kindTotal = workspace.displayed?.objects.filter(object => object.kind === kind).length ?? 0;
  const advancedFilterCount = [operatingSystem, skuId, adAccountState, entraAccountState, intuneCompliance, intuneManagement, identityState, storedEvidence]
    .filter(Boolean).length;
  const intuneValues = (field: 'complianceState' | 'managementState') => [...new Set(workspace.displayed?.objects
    .flatMap(object => object.observations.filter(row => row.source === 'INTUNE').map(row => row[field]))
    .filter((value): value is string => Boolean(value)) ?? [])].sort();
  const change = (name: string, value: string) => setParameters(previous => {
    const next = new URLSearchParams(previous); if (value) next.set(name, value); else next.delete(name);
    if (name !== 'page') next.delete('page'); return next;
  });
  const clearFilters = () => setParameters(previous => {
    const next = new URLSearchParams();
    for (const name of ['tenant', 'source']) { const value = previous.get(name); if (value) next.set(name, value); }
    return next;
  });
  useLayoutEffect(() => {
    const container = root.current?.closest('[data-testid="application-scroll-container"]');
    return () => {
      restored.current = false;
      if (container) { positions.delete(location.key); positions.set(location.key, { scroll: container.scrollTop,
        focus: document.activeElement?.getAttribute('data-object-focus') ?? null });
        if (positions.size > 50) positions.delete(positions.keys().next().value!); }
    };
  }, [location.key]);
  useLayoutEffect(() => {
    if (!result || restored.current) return;
    restored.current = true;
    const saved = positions.get(workingSetRestoreKey(location.state) ?? location.key);
    const container = root.current?.closest('[data-testid="application-scroll-container"]');
    if (saved && container) {
      const target = [...root.current?.querySelectorAll<HTMLElement>('[data-object-focus]') ?? []]
        .find(element => element.dataset.objectFocus === saved.focus);
      target?.focus({ preventScroll: true }); container.scrollTop = saved.scroll;
    }
  }, [result, location.key]);
  const columns: DataColumn<WorkingSetObject>[] = [
    { id: 'name', header: kind === 'DEVICE' ? 'Device / address' : kind === 'USER' ? 'Account' : 'Group', sortable: true, cell: row => {
      const primary = row.references.find(reference => !source || reference.source === source) ?? row.references[0];
      const principalNames = [...new Set(row.observations.map(observation => observation.userPrincipalName).filter((value): value is string => Boolean(value)))];
      return <div className="min-w-48">{primary ? <Link data-object-focus={objectPath(primary)} className="font-semibold text-accent-400 underline"
        to={objectPath(primary)} state={workingSetEntryState(location, workspace.directoryEndpoint)}>{row.label}</Link> : <strong>{row.label}</strong>}
        {principalNames.length > 0 && <p className="truncate text-xs text-slate-300">{principalNames.join(' · ')}</p>}
        {!primary && <p className="text-xs text-warn-400">Stable scoped identity unavailable</p>}
      </div>;
    } },
    { header: 'Task facts', cell: row => <div className="min-w-48 text-xs">{row.observations.map((observation, index) => <div key={index}>
      {kind === 'USER' && <><span>{observation.accountEnabled === null ? 'Account state unknown' : observation.accountEnabled ? 'Enabled' : 'Disabled'}</span>{observation.department && <span> · {observation.department}</span>}</>}
      {kind === 'DEVICE' && observation.source === 'INTUNE' && <><span>{observation.userPrincipalName ?? 'No associated user returned'} · {observation.complianceState ?? 'Compliance unknown'}</span>{observation.lastSyncAtUtc && <p className="text-muted">Last sync {new Date(observation.lastSyncAtUtc).toLocaleString()}</p>}</>}
      {kind === 'DEVICE' && observation.source === 'ENTRA' && <><span>{[observation.operatingSystem, observation.operatingSystemVersion, observation.trustType].filter(Boolean).join(' · ') || 'Device details not supplied'}</span>{observation.lastSignInAtUtc && <p className="text-muted">Last sign-in {new Date(observation.lastSignInAtUtc).toLocaleString()}</p>}</>}
      {kind === 'DEVICE' && observation.source !== 'INTUNE' && observation.source !== 'ENTRA' && <span>{observation.operatingSystem ?? observation.storedEvidence?.replace('_', ' ').toLowerCase() ?? (observation.accountEnabled === null ? 'Status not supplied' : observation.accountEnabled ? 'Enabled' : 'Disabled')}</span>}
      {kind === 'GROUP' && <span>{observation.groupTypes || 'Type not supplied'} · Security {observation.securityEnabled == null ? 'unknown' : observation.securityEnabled ? 'yes' : 'no'} · Mail {observation.mailEnabled == null ? 'unknown' : observation.mailEnabled ? 'yes' : 'no'}</span>}
    </div>)}</div> },
    { header: 'Sources', cell: row => <div className="flex max-w-64 flex-wrap gap-1 text-xs">{[...new Set(row.observations.map(observation => observation.source))].map(value => {
      const observation = row.observations.find(candidate => candidate.source === value)!;
      const read = workspace.displayed?.reads.find(candidate => candidate.key === observation.readKey);
      const state = read?.availability !== 'available' ? read?.availability ?? 'unknown' : read.coverage !== 'complete' ? read.coverage
        : read.freshUntilUtc && Date.parse(read.freshUntilUtc) <= Date.now() ? 'stale' : 'current';
      const age = compactAge(read?.retrievedAtUtc ?? observation.observedAtUtc ?? null);
      return <span key={value} className="rounded bg-slate-800 px-1.5 py-0.5">{workingSetSourceLabel[value]} · {state}{age ? ` · ${age}` : ''}</span>;
    })}</div> },
    { header: 'Exceptions & details', cell: row => <div className="max-w-72 text-xs">
      {row.hasCandidates && <p className="text-warn-400">Similar records remain separate candidates.</p>}
      {row.duplicateSourceIdentity && <p className="text-warn-400">Duplicate native ID in one source result.</p>}
      {row.conflictingIdentityEvidence && <p className="text-warn-400">Conflicting identity values retained.</p>}
      {row.references.length > 0 && row.references.every(reference => reference.source === 'WEC') && <p className="text-warn-400">Stored address; physical identity unconfirmed.</p>}
      <details className="mt-1"><summary className="cursor-pointer text-accent-400">IDs and provenance</summary>
        <ul className="mt-1 space-y-1">{row.references.map(reference => <li key={objectPath(reference)} className="break-all">
          <Link className="text-accent-400 underline" to={objectPath(reference)} state={workingSetEntryState(location, workspace.directoryEndpoint)}>{objectSourceLabel[reference.source]} · {reference.id}</Link>
        </li>)}{row.observations.filter(observation => observation.managementReference).map((observation, index) => <li key={index}>
          <Link className="text-accent-400 underline" to={managementRecordPath(observation.managementReference!)} state={workingSetEntryState(location, workspace.directoryEndpoint)}>
            {workingSetSourceLabel[observation.source]} source record {observation.managementReference!.recordIndex + 1}</Link>
        </li>)}</ul>
      </details>
    </div> },
  ];
  return <div ref={root} className="space-y-4"><PageHeader title={titles[kind]} subtitle="Source-scoped objects in the current working set" />
    {kind === 'DEVICE' && <nav aria-label="Device work modes" className="flex flex-wrap items-center gap-2 border-b border-slate-800 pb-3 text-sm">
      <Link aria-current="page" className="rounded bg-accent-600 px-3 py-2 font-medium text-white" to="/devices">Source records</Link>
      <Link className="rounded px-3 py-2 font-medium text-slate-200 hover:bg-slate-800" to="/clients">Operational view</Link>
      <Link className="rounded px-3 py-2 text-accent-400 hover:bg-slate-800" to="/clients/compare">Compare</Link>
      <Link className="rounded px-3 py-2 text-accent-400 hover:bg-slate-800" to="/cleanup">Device Cleanup</Link>
      <Link className="rounded px-3 py-2 text-accent-400 hover:bg-slate-800" to="/activedirectory">AD analysis</Link>
    </nav>}
    <nav aria-label="Object workspaces" className="flex flex-wrap gap-4 text-sm text-accent-400 underline">
      <Link to="/devices">Devices</Link><Link to="/users">Users</Link><Link to="/groups">Groups</Link>
      {kind === 'USER' && <Link to="/users/directory">AD query workspace</Link>}
      {kind === 'GROUP' && <Link to="/groups/directory">AD group query workspace</Link>}
    </nav>
    <WorkingSetCoverage kind={kind} kindTotal={kindTotal} matchTotal={result?.total ?? 0} /><WorkingSetSourceControls key={`${kind}:${directoryDiscovery ? query : ''}`} kind={kind}
      initialSearch={directoryDiscovery ? query : ''} initiallyExpanded={directoryDiscovery} />
    {tenantMismatch && <p role="alert" className="text-warn-400">This link belongs to tenant {requestedTenant}. Select that tenant in Data sources before reviewing its loaded assignments.</p>}
    <div className="flex flex-wrap items-end gap-3">
      <label className="text-xs text-muted">Search loaded {titles[kind].toLowerCase()}<Input value={query} onChange={event => change('q', event.target.value)} /></label>
      <label className="text-xs text-muted">Source<Select value={source ?? ''} onChange={event => change('source', event.target.value)}><option value="">All loaded sources</option>
        {sources.filter(value => kind === 'DEVICE' || value === 'ACTIVE_DIRECTORY' || value === 'ENTRA').map(value => <option key={value} value={value}>{workingSetSourceLabel[value]}</option>)}</Select></label>
      {kind === 'USER' && <label className="text-xs text-muted">Account state<Select value={accountState ?? ''} onChange={event => change('account', event.target.value)}>
        <option value="">Any / conflicting</option><option value="enabled">Enabled</option><option value="disabled">Disabled</option><option value="unknown">Unknown / not supplied</option></Select></label>}
      <details open={advancedFilterCount > 0 ? true : undefined} className="min-w-full rounded border border-slate-800 p-2"><summary className="cursor-pointer text-sm text-accent-400">More filters{advancedFilterCount > 0 ? ` (${advancedFilterCount} active)` : ''}</summary>
      <div className="mt-2 flex flex-wrap items-end gap-3">
      {kind === 'DEVICE' && <label className="text-xs text-muted">Operating system (exact)<Input value={operatingSystem} onChange={event => change('os', event.target.value)} /></label>}
      {kind === 'DEVICE' && <>
        {([['adAccount', 'AD account state', adAccountState], ['entraAccount', 'Entra account state', entraAccountState]] as const).map(([key, label, value]) =>
          <label key={key} className="text-xs text-muted">{label}<Select value={value ?? ''} onChange={event => change(key, event.target.value)}>
            <option value="">Any loaded state</option><option value="enabled">Enabled</option><option value="disabled">Disabled</option><option value="unknown">Unknown in source record</option>
          </Select></label>)}
        {([['compliance', 'Intune compliance', 'complianceState', intuneCompliance], ['management', 'Intune management', 'managementState', intuneManagement]] as const).map(([key, label, field, value]) =>
          <label key={key} className="text-xs text-muted">{label}<Select value={value} onChange={event => change(key, event.target.value)}>
            <option value="">Any loaded state</option><option value="__unknown">Unknown in Intune record</option>
            {[...new Set([...intuneValues(field), ...(value && value !== '__unknown' ? [value] : [])])].map(state => <option key={state} value={state}>{state}</option>)}
          </Select></label>)}
        <label className="text-xs text-muted">Stored evidence<Select value={storedEvidence ?? ''} onChange={event => change('stored', event.target.value)}>
          <option value="">Any</option><option value="saved">Saved target</option><option value="scanned">Stored Inventory or Security scan</option>
        </Select></label>
      </>}
      <label className="text-xs text-muted">Identity assessment<Select value={identityState ?? ''} onChange={event => change('identity', event.target.value)}>
        <option value="">Any</option><option value="unresolved">No scoped identity</option><option value="candidates">Candidates / ambiguous association</option><option value="conflict">Conflicting or duplicate identity</option>
      </Select></label>
      {kind === 'USER' && <label className="text-xs text-muted">Assigned SKU ID<Input value={skuId} onChange={event => change('sku', event.target.value)} /></label>}
      </div></details>
      {(query || accountState || advancedFilterCount > 0) && <button type="button" className="text-sm text-accent-400 underline" onClick={clearFilters}>Clear filters (keep source scope)</button>}
    </div>
    <p className="text-xs text-muted">Showing {result?.rows.length ?? 0} of {result?.total ?? 0} matching {titles[kind].toLowerCase()} from {kindTotal} loaded. A missing match does not prove absence from an unloaded source.</p>
    <DataTable columns={columns} rows={result?.rows ?? []} getRowKey={row => row.key} emptyMessage="No matching loaded objects. Check source coverage or explicitly load another source page."
      sort={{ column: 'name', direction: descending ? 'desc' : 'asc' }} onSortChange={sort => change('sort', sort.direction)}
      pagination={{ page: result?.page ?? 1, pageSize, total: result?.total ?? 0, itemLabel: 'working-set objects',
        onPageChange: page => change('page', String(page)), onPageSizeChange: size => change('size', String(size)) }} />
  </div>;
}

export function DevicesWorkingSetPage() { return <ObjectWorkingSetPage kind="DEVICE" />; }
export function UsersWorkingSetPage() { return <ObjectWorkingSetPage kind="USER" />; }
export function GroupsWorkingSetPage() { return <ObjectWorkingSetPage kind="GROUP" />; }
