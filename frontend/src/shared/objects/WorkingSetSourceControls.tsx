import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import type { CachedDirectoryGroupPage, CachedDirectoryUserList, Microsoft365Resource, Microsoft365Status, ObjectKind } from '../api-types.generated';
import { invokeCancellable, type CancellableBridgeInvocation } from '../bridge/bridgeClient';
import { presentError } from '../bridge/errorPresentation';
import { useTargets } from '../targets/TargetContext';
import { Button } from '../ui/Button';
import { Input } from '../ui/Input';
import { toUserDirectoryConnection } from '../../features/users/users';
import { useWorkingSet } from './WorkingSetContext';
import { directoryGroupWorkingSetRead, directoryUserWorkingSetRead } from './workingSetSources';

export function WorkingSetSourceControls({ kind }: { kind: ObjectKind }) {
  const workspace = useWorkingSet();
  const { adminCredentials } = useTargets();
  const [endpoint, setEndpoint] = useState(workspace.directoryEndpoint ?? { domain: '', server: '' });
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const active = useRef<CancellableBridgeInvocation<unknown> | null>(null);
  const generation = useRef(0);
  const cancel = () => { generation.current++; active.current?.cancel(); active.current = null; setBusy(false); };
  const changeEndpoint = (next: typeof endpoint) => {
    if (workspace.directoryEndpoint) workspace.clearFamily('directory');
    setEndpoint(next);
  };
  useEffect(() => { cancel(); return () => { generation.current++; active.current?.cancel(); active.current = null; }; }, [adminCredentials, workspace.cloudGeneration, workspace.directoryGeneration]);
  const readDirectory = async () => {
    const own = ++generation.current; active.current?.cancel(); setBusy(true); setError(null);
    const selection = { scope: endpoint.domain.trim(), search, page, pageSize: 100 };
    const payload = { connection: toUserDirectoryConnection(endpoint, adminCredentials), directoryScope: selection.scope, search, page, pageSize: 100, refresh: true };
    try {
      if (kind === 'USER') {
        const request = invokeCancellable<CachedDirectoryUserList>('usermanagement', 'readDirectoryPage', payload); active.current = request;
        const read = await request.promise;
        if (own === generation.current) workspace.publishDirectory(directoryUserWorkingSetRead(read, selection), endpoint);
      } else {
        const request = invokeCancellable<CachedDirectoryGroupPage>('groups', 'readDirectoryPage', payload); active.current = request;
        const read = await request.promise;
        if (own === generation.current) workspace.publishDirectory(directoryGroupWorkingSetRead(read, selection), endpoint);
      }
    } catch (caught) { if (own === generation.current) setError(presentError(caught).message); }
    finally { if (own === generation.current) { active.current = null; setBusy(false); } }
  };
  const readCloud = async (resource: Microsoft365Resource) => {
    const own = ++generation.current; active.current?.cancel(); setBusy(true); setError(null);
    try {
      const status = invokeCancellable<Microsoft365Status>('microsoft365', 'getStatus'); active.current = status;
      const value = await status.promise;
      if (own !== generation.current) return;
      const request = invokeCancellable('microsoft365', 'read', { resource, tenantId: value.connection.configuration.tenantId, refresh: true }); active.current = request;
      await request.promise;
      if (own === generation.current) await workspace.refreshCached(true);
    } catch (caught) {
      if (own === generation.current) { setError(presentError(caught).message); await workspace.refreshCached(true); }
    } finally { if (own === generation.current) { active.current = null; setBusy(false); } }
  };
  return <details className="rounded-lg border border-slate-800 p-3 text-sm"><summary className="cursor-pointer text-accent-400">Load a source into this working set</summary>
    <p className="my-3 text-xs text-muted">Only these explicit actions query a directory or Microsoft Graph. Search below filters loaded data. Each AD read adds one bounded page; changing its query replaces that collection.</p>
    {kind !== 'DEVICE' && <div className="mb-3 flex flex-wrap items-end gap-3">
      <label>Directory DNS scope<Input value={endpoint.domain} disabled={busy} onChange={event => changeEndpoint({ ...endpoint, domain: event.target.value })} placeholder="example.test" /></label>
      <label>Directory server (optional)<Input value={endpoint.server} disabled={busy} onChange={event => changeEndpoint({ ...endpoint, server: event.target.value })} /></label>
      <label>Source query<Input value={search} disabled={busy} onChange={event => { setSearch(event.target.value); setPage(1); }} /></label>
      <label>Source page<Input type="number" min={1} value={page} disabled={busy} onChange={event => setPage(Math.max(1, Math.trunc(Number(event.target.value)) || 1))} className="w-24" /></label>
      <Button disabled={busy || !endpoint.domain.trim()} onClick={() => void readDirectory()}>Read AD page (up to 100)</Button>
    </div>}
    {kind === 'DEVICE' && <div className="mb-3 flex flex-wrap items-end gap-3">
      <label>Stored address query<Input value={search} maxLength={100} onChange={event => setSearch(event.target.value)} /></label>
      <Button disabled={workspace.busy || !search.trim()} onClick={() => void workspace.refreshCached(true, search.trim())}>Load matching stored addresses</Button>
    </div>}
    <div className="flex flex-wrap items-center gap-3">
      <Button disabled={busy} onClick={() => void readCloud(kind === 'USER' ? 'USERS' : kind === 'GROUP' ? 'GROUPS' : 'DEVICES')}>Read bounded Entra inventory</Button>
      {kind === 'DEVICE' && <Button disabled={busy} onClick={() => void readCloud('MANAGED_DEVICES')}>Read bounded Intune inventory</Button>}
      <Link className="text-accent-400 underline" to="/microsoft365">Microsoft 365 connection and source analysis</Link>
      {busy && <Button onClick={cancel}>Cancel source read</Button>}
    </div>
    {error && <p role="alert" className="mt-3 text-fail-400">{error}</p>}
  </details>;
}
