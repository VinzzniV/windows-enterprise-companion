import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import type { CachedDirectoryGroupPage, Microsoft365Snapshot, Microsoft365Status } from '../../shared/api-types.generated';
import { invokeCancellable, type CancellableBridgeInvocation } from '../../shared/bridge/bridgeClient';
import { presentError } from '../../shared/bridge/errorPresentation';
import { objectPath } from '../../shared/objects/objectRoutes';
import { SourceReadState } from '../../shared/objects/SourceReadState';
import { Button } from '../../shared/ui/Button';
import { Card } from '../../shared/ui/Card';
import { Input } from '../../shared/ui/Input';
import { PageHeader } from '../../shared/ui/PageHeader';
import { DirectoryGroupState } from './DirectoryGroupState';
import { useGroupDirectoryConnection } from './useGroupProfile';

export function GroupsPage() {
  const connection = useGroupDirectoryConnection();
  const [scope, setScope] = useState(connection?.domain ?? '');
  const [search, setSearch] = useState('');
  const [ad, setAd] = useState<CachedDirectoryGroupPage | null>(null);
  const [cloud, setCloud] = useState<Microsoft365Snapshot | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [now, setNow] = useState(Date.now);
  const generation = useRef(0);
  const active = useRef<CancellableBridgeInvocation<unknown> | null>(null);
  useEffect(() => {
    generation.current++; active.current?.cancel(); setAd(null); setBusy(false); setError(null);
    return () => { generation.current++; active.current?.cancel(); };
  }, [connection]);
  useEffect(() => { const timer = window.setInterval(() => setNow(Date.now()), 1000); return () => window.clearInterval(timer); }, []);
  const load = async (source: 'ad' | 'entra', page = 1) => {
    const own = ++generation.current; active.current?.cancel(); setBusy(true); setError(null);
    try {
      if (source === 'ad') {
        const request = invokeCancellable<CachedDirectoryGroupPage>('groups', 'readDirectoryPage', { connection, directoryScope: scope, search, page, refresh: true });
        active.current = request; const value = await request.promise;
        if (own === generation.current) setAd(value);
      } else {
        const status = invokeCancellable<Microsoft365Status>('microsoft365', 'getStatus'); active.current = status;
        const session = await status.promise;
        if (own !== generation.current) return;
        const request = invokeCancellable<Microsoft365Snapshot>('microsoft365', 'read', { resource: 'GROUPS', tenantId: session.connection.configuration.tenantId, refresh: true });
        active.current = request; const value = await request.promise;
        if (own === generation.current) setCloud(value);
      }
    } catch (caught) { if (own === generation.current) { setError(presentError(caught).message); if (source === 'ad') setAd(null); else setCloud(null); } }
    finally { if (own === generation.current) { setBusy(false); active.current = null; } }
  };
  const adData = ad?.state.retainedUntilUtc && Date.parse(ad.state.retainedUntilUtc) <= now ? null : ad?.data;
  const cloudData = cloud?.state?.retainedUntilUtc && Date.parse(cloud.state.retainedUntilUtc) <= now ? null : cloud?.data;
  const tenant = cloud?.state?.tenantId;
  return <div className="space-y-4"><PageHeader title="Groups" subtitle="Source-scoped group discovery and direct membership" />
    <Link className="text-sm text-accent-400 underline" to="/groups">Open the shared group working set</Link>
    <p className="text-sm text-muted">These source queries remain separate. Counts describe the selected AD query or loaded Entra set; they are not a combined directory total.</p>
    {error && <p role="alert" className="text-fail-400">{error}</p>}
    {busy && <Button onClick={() => { generation.current++; active.current?.cancel(); active.current = null; setBusy(false); }}>Cancel read</Button>}
    <Card title="Active Directory groups">
      <div className="mb-3 flex flex-wrap items-end gap-3">
        <label className="text-xs text-muted">Directory DNS scope<Input value={scope} disabled={busy} onChange={event => { setScope(event.target.value); setAd(null); }} placeholder="example.test" /></label>
        <label className="text-xs text-muted">Group search<Input value={search} disabled={busy} onChange={event => { setSearch(event.target.value); setAd(null); }} /></label>
        <Button disabled={busy || !scope.trim()} onClick={() => void load('ad')}>Read bounded AD group page</Button>
      </div>
      {ad && <DirectoryGroupState state={ad.state} now={now} />}
      {adData && <>
        <div className="mb-3 flex items-center gap-3 text-sm">
          <Button disabled={busy || adData.page <= 1} onClick={() => void load('ad', adData.page - 1)}>Previous AD page</Button>
          <span>Page {adData.page} · {adData.groups.length} displayed · {adData.totalCount} visible query matches</span>
          <Button disabled={busy || adData.page * adData.pageSize >= adData.totalCount} onClick={() => void load('ad', adData.page + 1)}>Next AD page</Button>
        </div>
        <ul className="max-h-96 space-y-2 overflow-auto">{adData.groups.map((group, index) => <li key={index} className="text-sm">
          {group.objectId ? <Link className="text-accent-400 underline" state={{ directoryEndpoint: { domain: scope, server: connection?.server ?? '' } }}
            to={objectPath({ kind: 'GROUP', source: 'ACTIVE_DIRECTORY', scope: group.directoryScope, id: group.objectId })}>{group.name}</Link> : <span>{group.name} · GUID unavailable</span>}
          <p className="break-all text-xs text-muted">{group.distinguishedName}</p>
        </li>)}</ul>
      </>}
    </Card>
    <Card title="Entra groups">
      <Button disabled={busy} onClick={() => void load('entra')}>Read bounded Entra group inventory</Button>
      <Link className="ml-3 text-sm text-accent-400 underline" to="/microsoft365">Connection and source queries</Link>
      {cloud?.state && <SourceReadState state={cloud.state} now={now} />}
      {cloudData && <ul className="mt-3 max-h-96 space-y-2 overflow-auto">{cloudData.groups.map((group, index) => <li key={index} className="text-sm">
        {group.id && tenant ? <Link className="text-accent-400 underline" to={objectPath({ kind: 'GROUP', source: 'ENTRA', scope: tenant, id: group.id })}>{group.displayName ?? group.id}</Link>
          : <span>{group.displayName ?? 'Limited-information group'} · Scoped ID unavailable</span>}
      </li>)}</ul>}
    </Card>
  </div>;
}
