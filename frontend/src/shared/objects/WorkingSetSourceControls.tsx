import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import type { CachedDirectoryGroupPage, CachedDirectoryUserList, Microsoft365Resource, Microsoft365Status, ObjectKind, ReadAdComputerListResult } from '../api-types.generated';
import { invokeCancellable, type CancellableBridgeInvocation } from '../bridge/bridgeClient';
import { presentError } from '../bridge/errorPresentation';
import { useTargets } from '../targets/TargetContext';
import { useEnvironmentOptional } from '../environment/EnvironmentContext';
import { Button } from '../ui/Button';
import { Input } from '../ui/Input';
import { toUserDirectoryConnection } from '../../features/users/users';
import { useWorkingSet } from './WorkingSetContext';
import { directoryComputerWorkingSetRead, directoryGroupWorkingSetRead, directoryUserWorkingSetRead } from './workingSetSources';

export function WorkingSetSourceControls({ kind, initialSearch = '', initiallyExpanded = false }: {
  kind: ObjectKind;
  initialSearch?: string;
  initiallyExpanded?: boolean;
}) {
  const workspace = useWorkingSet();
  const environment = useEnvironmentOptional();
  const { adminCredentials } = useTargets();
  const [endpoint, setEndpoint] = useState(workspace.directoryEndpoint ?? { domain: '', server: '' });
  const [search, setSearch] = useState(initialSearch);
  const [storedSearch, setStoredSearch] = useState('');
  const [page, setPage] = useState(1);
  const [expanded, setExpanded] = useState(initiallyExpanded);
  const [operation, setOperation] = useState<'directory' | 'cloud' | 'management' | null>(null);
  const [error, setError] = useState<string | null>(null);
  const active = useRef<CancellableBridgeInvocation<unknown> | null>(null);
  const activeOwner = useRef<typeof operation>(null);
  const generation = useRef(0);
  const managementActive = operation === 'management' || operation === null && environment?.loading === true;
  const busy = operation !== null || environment?.loading === true;
  const begin = (owner: Exclude<typeof operation, null>) => { activeOwner.current = owner; setOperation(owner); };
  const finish = () => { activeOwner.current = null; active.current = null; setOperation(null); };
  const cancel = () => {
    generation.current++;
    if (activeOwner.current === 'management' || activeOwner.current === null && environment?.loading) environment?.cancel();
    else active.current?.cancel();
    finish();
  };
  const changeEndpoint = (next: typeof endpoint) => {
    if (workspace.directoryEndpoint) workspace.clearFamily('directory');
    setEndpoint(next);
  };
  useEffect(() => { cancel(); return () => { generation.current++; active.current?.cancel(); active.current = null; }; }, [adminCredentials, workspace.cloudGeneration, workspace.directoryGeneration, workspace.managementGeneration]);
  const readManagement = async () => {
    if (!environment) return;
    const own = ++generation.current; begin('management'); setError(null);
    try { await environment.refresh(); if (own === generation.current) await workspace.refreshCached(true); }
    catch (caught) { if (own === generation.current) setError(presentError(caught).message); }
    finally { if (own === generation.current) finish(); }
  };
  const readDirectory = async () => {
    const own = ++generation.current; active.current?.cancel(); begin('directory'); setError(null);
    const selection = { scope: endpoint.domain.trim(), search, page, pageSize: 100 };
    const payload = { connection: toUserDirectoryConnection(endpoint, adminCredentials), directoryScope: selection.scope, search, page, pageSize: 100, refresh: true };
    try {
      if (kind === 'DEVICE') {
        const request = invokeCancellable<ReadAdComputerListResult>('activedirectory', 'readComputerList', {
          connection: payload.connection, directoryScope: selection.scope, search, limit: 100, refresh: true,
        }); active.current = request;
        const read = await request.promise;
        if (own === generation.current) workspace.publishDirectory(directoryComputerWorkingSetRead(read), endpoint);
      } else if (kind === 'USER') {
        const request = invokeCancellable<CachedDirectoryUserList>('usermanagement', 'readDirectoryPage', payload); active.current = request;
        const read = await request.promise;
        if (own === generation.current) workspace.publishDirectory(directoryUserWorkingSetRead(read, selection), endpoint);
      } else {
        const request = invokeCancellable<CachedDirectoryGroupPage>('groups', 'readDirectoryPage', payload); active.current = request;
        const read = await request.promise;
        if (own === generation.current) workspace.publishDirectory(directoryGroupWorkingSetRead(read, selection), endpoint);
      }
    } catch (caught) { if (own === generation.current) setError(presentError(caught).message); }
    finally { if (own === generation.current) finish(); }
  };
  const readCloud = async (resource: Microsoft365Resource) => {
    const own = ++generation.current; active.current?.cancel(); begin('cloud'); setError(null);
    try {
      const status = invokeCancellable<Microsoft365Status>('microsoft365', 'getStatus'); active.current = status;
      const value = await status.promise;
      if (own !== generation.current) return;
      const request = invokeCancellable('microsoft365', 'read', { resource, tenantId: value.connection.configuration.tenantId, refresh: true }); active.current = request;
      await request.promise;
      if (own === generation.current) await workspace.refreshCached(true);
    } catch (caught) {
      if (own === generation.current) { setError(presentError(caught).message); await workspace.refreshCached(true); }
    } finally { if (own === generation.current) finish(); }
  };
  const directoryAction = kind === 'DEVICE' ? 'Search selected AD source for computers (up to 100)'
    : kind === 'USER' ? 'Search selected AD source for users (page of 100)'
      : 'Search selected AD source for groups (page of 100)';
  return <details open={expanded} onToggle={event => setExpanded(event.currentTarget.open)} className="rounded-lg border border-slate-800 p-3 text-sm"><summary className="cursor-pointer text-accent-400">Load or search a source</summary>
    <p className="my-3 text-xs text-muted">Only these explicit actions query external sources. Search below filters loaded data. Each AD read is bounded; computer searches return up to 100 matches. Use a narrower query to find records outside that result.</p>
    <div className="mb-3 flex flex-wrap items-end gap-3">
      <label>Directory DNS scope<Input value={endpoint.domain} disabled={busy} onChange={event => changeEndpoint({ ...endpoint, domain: event.target.value })} placeholder="example.test" /></label>
      <label>Directory server (optional)<Input value={endpoint.server} disabled={busy} onChange={event => changeEndpoint({ ...endpoint, server: event.target.value })} /></label>
      <label>Source query<Input value={search} disabled={busy} onChange={event => { setSearch(event.target.value); setPage(1); }} /></label>
      {kind !== 'DEVICE' && <label>Source page<Input type="number" min={1} value={page} disabled={busy} onChange={event => setPage(Math.max(1, Math.trunc(Number(event.target.value)) || 1))} className="w-24" /></label>}
      <Button disabled={busy || !endpoint.domain.trim()} onClick={() => void readDirectory()}>{directoryAction}</Button>
    </div>
    {kind === 'DEVICE' && <div className="mb-3 flex flex-wrap items-end gap-3">
      <label>Stored address query<Input value={storedSearch} maxLength={100} onChange={event => setStoredSearch(event.target.value)} /></label>
      <Button disabled={workspace.busy || !storedSearch.trim()} onClick={() => void workspace.refreshCached(true, storedSearch.trim())}>Load matching stored addresses</Button>
      {environment && <Button disabled={environment.loading || busy} onClick={() => void readManagement()}>Read configured management sources</Button>}
      {environment?.error && <p role="alert" className="text-fail-400">{environment.error.message}</p>}
    </div>}
    <div className="flex flex-wrap items-center gap-3">
      <Button disabled={busy} onClick={() => void readCloud(kind === 'USER' ? 'USERS' : kind === 'GROUP' ? 'GROUPS' : 'DEVICES')}>Read bounded Entra inventory</Button>
      {kind === 'DEVICE' && <Button disabled={busy} onClick={() => void readCloud('MANAGED_DEVICES')}>Read bounded Intune inventory</Button>}
      <Link className="text-accent-400 underline" to="/microsoft365">Microsoft 365 connection and source analysis</Link>
      {busy && <><span role="status" className="text-xs text-muted">{managementActive ? 'Reading configured management sources…'
        : operation === 'directory' ? 'Reading the selected Active Directory source…' : 'Reading the selected Microsoft 365 source…'}</span>
        <Button onClick={cancel}>{managementActive ? 'Cancel management source read' : operation === 'directory' ? 'Cancel AD source read' : 'Cancel Microsoft 365 source read'}</Button></>}
    </div>
    {error && <p role="alert" className="mt-3 text-fail-400">{error}</p>}
  </details>;
}
