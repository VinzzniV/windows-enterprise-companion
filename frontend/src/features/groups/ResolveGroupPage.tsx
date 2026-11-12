import { useEffect, useRef, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import type { ObjectReference } from '../../shared/api-types.generated';
import { invokeCancellable, type CancellableBridgeInvocation } from '../../shared/bridge/bridgeClient';
import { presentError } from '../../shared/bridge/errorPresentation';
import { objectPath } from '../../shared/objects/objectRoutes';
import { Button } from '../../shared/ui/Button';
import { Card } from '../../shared/ui/Card';
import { PageHeader } from '../../shared/ui/PageHeader';
import { useGroupDirectoryConnection } from './useGroupProfile';

export function ResolveGroupPage() {
  const [params] = useSearchParams();
  const scope = params.get('scope') ?? '';
  const dn = params.get('dn');
  const sid = params.get('sid');
  const connection = useGroupDirectoryConnection();
  const navigate = useNavigate();
  const active = useRef<CancellableBridgeInvocation<ObjectReference> | null>(null);
  const generation = useRef(0);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    generation.current++; active.current?.cancel(); setBusy(false); setError(null);
    return () => { generation.current++; active.current?.cancel(); };
  }, [scope, dn, sid, connection]);
  const resolve = async () => {
    const own = ++generation.current; setBusy(true); setError(null);
    const request = invokeCancellable<ObjectReference>('groups', 'resolve', { directoryScope: scope, distinguishedName: dn, securityIdentifier: sid, connection });
    active.current = request;
    try { const reference = await request.promise;
      if (own === generation.current) navigate(objectPath(reference), { state: { directoryEndpoint: { domain: connection?.domain ?? scope, server: connection?.server ?? '' } } });
    } catch (caught) { if (own === generation.current) setError(presentError(caught).message); }
    finally { if (own === generation.current) { setBusy(false); active.current = null; } }
  };
  return <div className="space-y-4"><PageHeader title="Resolve AD group" subtitle={scope || 'Directory scope unavailable'} />
    <Card title="Exact source reference">
      <p className="mb-3 break-all text-sm">{dn ?? sid ?? 'No identity supplied'}</p>
      <p className="mb-3 text-xs text-muted">This bounded read resolves the source reference to a native GUID. Display names never select a group; an ambiguous result remains unresolved.</p>
      <Button disabled={busy || !scope || (!dn && !sid)} onClick={() => void resolve()}>Resolve and open group</Button>
      {busy && <Button onClick={() => { generation.current++; active.current?.cancel(); active.current = null; setBusy(false); }}>Cancel read</Button>}
      {error && <p role="alert" className="mt-3 text-fail-400">{error}</p>}
    </Card></div>;
}
