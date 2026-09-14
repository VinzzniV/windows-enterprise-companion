import { useLayoutEffect, useMemo, useRef } from 'react';
import { Link, useLocation, useSearchParams } from 'react-router-dom';
import type { ObjectKind, ObjectSource } from '../api-types.generated';
import { objectPath, objectSourceLabel } from './objectRoutes';
import { useWorkingSet } from './WorkingSetContext';
import { queryWorkingSet, type WorkingSetObject } from './workingSet';
import { WorkingSetCoverage } from './WorkingSetCoverage';
import { WorkingSetSourceControls } from './WorkingSetSourceControls';
import { DataTable, type DataColumn } from '../ui/DataTable';
import { Input } from '../ui/Input';
import { Select } from '../ui/Select';
import { PageHeader } from '../ui/PageHeader';

const positions = new Map<string, { scroll: number; focus: string | null }>();
const sources: ObjectSource[] = ['ACTIVE_DIRECTORY', 'ENTRA', 'INTUNE', 'WEC'];
const titles: Record<ObjectKind, string> = { DEVICE: 'Devices', USER: 'Users', GROUP: 'Groups' };

export function ObjectWorkingSetPage({ kind }: { kind: ObjectKind }) {
  const workspace = useWorkingSet();
  const location = useLocation();
  const [parameters, setParameters] = useSearchParams();
  const root = useRef<HTMLDivElement>(null);
  const restored = useRef(false);
  const query = parameters.get('q') ?? '';
  const source = sources.find(value => value === parameters.get('source'));
  const accountState = ['enabled', 'disabled', 'unknown'].find(value => value === parameters.get('account')) as 'enabled' | 'disabled' | 'unknown' | undefined;
  const operatingSystem = parameters.get('os') ?? '';
  const skuId = parameters.get('sku') ?? '';
  const descending = parameters.get('sort') === 'desc';
  const pageSize = [25, 50, 100].find(value => value === Number(parameters.get('size'))) ?? 25;
  const result = useMemo(() => workspace.displayed ? queryWorkingSet(workspace.displayed, { kind, query, source, accountState,
    operatingSystem, skuId, descending, page: Number(parameters.get('page')) || 1, pageSize }) : null,
  [workspace.displayed, kind, query, source, accountState, operatingSystem, skuId, descending, parameters, pageSize]);
  const change = (name: string, value: string) => setParameters(previous => {
    const next = new URLSearchParams(previous); if (value) next.set(name, value); else next.delete(name);
    if (name !== 'page') next.delete('page'); return next;
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
    const saved = positions.get(location.key);
    const container = root.current?.closest('[data-testid="application-scroll-container"]');
    if (saved && container) {
      const target = [...root.current?.querySelectorAll<HTMLElement>('[data-object-focus]') ?? []]
        .find(element => element.dataset.objectFocus === saved.focus);
      target?.focus({ preventScroll: true }); container.scrollTop = saved.scroll;
    }
  }, [result, location.key]);
  const columns: DataColumn<WorkingSetObject>[] = [
    { id: 'name', header: 'Object / address', sortable: true, cell: row => <div className="min-w-48 space-y-1"><strong>{row.label}</strong>
      <div className="flex flex-col gap-1">{row.references.map(reference => <Link key={objectPath(reference)} data-object-focus={objectPath(reference)}
        className="break-all text-xs text-accent-400 underline" to={objectPath(reference)}
        state={{ directoryEndpoint: workspace.directoryEndpoint, fromWorkingSet: `${location.pathname}${location.search}` }}>
        {objectSourceLabel[reference.source]} · {reference.id}</Link>)}
        {row.references.length === 0 && <span className="text-warn-400">Scoped source identity unavailable</span>}</div></div> },
    { header: 'Source evidence', cell: row => <ul className="space-y-2 text-xs">{row.observations.map((observation, index) => <li key={index}>
      <strong>{objectSourceLabel[observation.source]}</strong> · {observation.label}
      <p className="break-all text-muted">{observation.reference?.scope ?? 'Scope unavailable'}{observation.operatingSystem ? ` · ${observation.operatingSystem}` : ''}</p>
      {kind !== 'GROUP' && <p>Account: {observation.accountEnabled === null ? 'Unknown / not supplied' : observation.accountEnabled ? 'Enabled' : 'Disabled'}</p>}
      {observation.observedAtUtc && <p>Observed: {new Date(observation.observedAtUtc).toLocaleString()}</p>}
    </li>)}</ul> },
    { header: 'Identity assessment', cell: row => <div className="max-w-64 space-y-1 text-xs">
      {row.hasCandidates && <p className="text-warn-400">Similar source records exist; candidate evidence does not confirm identity.</p>}
      {row.duplicateSourceIdentity && <p className="text-warn-400">Duplicate native ID in a source result. Inspect source records.</p>}
      {row.conflictingIdentityEvidence && <p className="text-warn-400">Conflicting identity values are retained.</p>}
      {row.references.every(reference => reference.source === 'WEC') && <p>Stored execution address; physical device identity unconfirmed.</p>}
      {row.references.length > 1 && <p>Related by verified scoped IDs and sufficient cached query coverage.</p>}
      {!row.hasCandidates && !row.duplicateSourceIdentity && !row.conflictingIdentityEvidence && row.references.length === 1 && row.references[0].source !== 'WEC' && <p>Scoped source object.</p>}
    </div> },
  ];
  return <div ref={root} className="space-y-4"><PageHeader title={titles[kind]} subtitle="Source-scoped objects in the current working set" />
    <nav aria-label="Object workspaces" className="flex flex-wrap gap-4 text-sm text-accent-400 underline">
      <Link to="/devices">Devices</Link><Link to="/users/workspace">Users</Link><Link to="/groups/workspace">Groups</Link>
      {kind === 'DEVICE' && <><Link to="/clients">Client posture, saved targets and batch scans</Link><Link to="/clients/compare">Compare</Link><Link to="/cleanup">Device Cleanup</Link></>}
      {kind === 'USER' && <Link to="/users">AD query workspace</Link>}
    </nav>
    <WorkingSetCoverage /><WorkingSetSourceControls kind={kind} />
    <div className="flex flex-wrap items-end gap-3">
      <label className="text-xs text-muted">Search loaded {titles[kind].toLowerCase()}<Input value={query} onChange={event => change('q', event.target.value)} /></label>
      <label className="text-xs text-muted">Source<Select value={source ?? ''} onChange={event => change('source', event.target.value)}><option value="">All loaded sources</option>
        {sources.filter(value => kind === 'DEVICE' || value !== 'INTUNE' && value !== 'WEC').map(value => <option key={value} value={value}>{objectSourceLabel[value]}</option>)}</Select></label>
      {kind !== 'GROUP' && <label className="text-xs text-muted">Account state<Select value={accountState ?? ''} onChange={event => change('account', event.target.value)}>
        <option value="">Any / conflicting</option><option value="enabled">Enabled</option><option value="disabled">Disabled</option><option value="unknown">Unknown / not supplied</option></Select></label>}
      {kind === 'DEVICE' && <label className="text-xs text-muted">Operating system (exact)<Input value={operatingSystem} onChange={event => change('os', event.target.value)} /></label>}
      {kind === 'USER' && <label className="text-xs text-muted">Assigned SKU ID<Input value={skuId} onChange={event => change('sku', event.target.value)} /></label>}
    </div>
    <p className="text-xs text-muted">{result?.total ?? 0} matches in this displayed working set. A missing match does not prove absence from an unloaded source.</p>
    <DataTable columns={columns} rows={result?.rows ?? []} getRowKey={row => row.key} emptyMessage="No matching loaded objects. Check source coverage or explicitly load another source page."
      sort={{ column: 'name', direction: descending ? 'desc' : 'asc' }} onSortChange={sort => change('sort', sort.direction)}
      pagination={{ page: result?.page ?? 1, pageSize, total: result?.total ?? 0, itemLabel: 'working-set objects',
        onPageChange: page => change('page', String(page)), onPageSizeChange: size => change('size', String(size)) }} />
  </div>;
}

export function DevicesWorkingSetPage() { return <ObjectWorkingSetPage kind="DEVICE" />; }
export function UsersWorkingSetPage() { return <ObjectWorkingSetPage kind="USER" />; }
export function GroupsWorkingSetPage() { return <ObjectWorkingSetPage kind="GROUP" />; }
