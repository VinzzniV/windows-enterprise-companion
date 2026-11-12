import { useEffect, useMemo, useRef, useState } from 'react';
import { Link, useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import type { ObjectReference } from '../../shared/api-types.generated';
import { invokeCancellable, type CancellableBridgeInvocation } from '../../shared/bridge/bridgeClient';
import { presentError } from '../../shared/bridge/errorPresentation';
import { objectPath } from '../../shared/objects/objectRoutes';
import { useTargets } from '../../shared/targets/TargetContext';
import { Button } from '../../shared/ui/Button';
import { Card } from '../../shared/ui/Card';
import { Input } from '../../shared/ui/Input';
import { PageHeader } from '../../shared/ui/PageHeader';
import { loadView } from '../../shared/viewCache';
import { emptyUserDirectoryEndpoint, toUserDirectoryConnection, userDirectoryViewKey, type UserDirectoryEndpoint } from './users';

export function ResolveObservedUserPage() {
  const [params] = useSearchParams();
  const sid = params.get('sid') ?? '';
  const location = useLocation();
  const navigate = useNavigate();
  const { adminCredentials } = useTargets();
  const endpoint = useMemo(() => loadView<UserDirectoryEndpoint>(userDirectoryViewKey) ?? emptyUserDirectoryEndpoint, []);
  const [scope, setScope] = useState(endpoint.domain);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const active = useRef<CancellableBridgeInvocation<ObjectReference> | null>(null);
  const generation = useRef(0);
  const connection = useMemo(() => toUserDirectoryConnection({ ...endpoint, domain: scope }, adminCredentials), [endpoint, scope, adminCredentials]);
  useEffect(() => {
    generation.current++; active.current?.cancel(); setBusy(false); setError(null);
    return () => { generation.current++; active.current?.cancel(); };
  }, [sid, connection]);
  const resolve = async () => {
    const own = ++generation.current;
    setBusy(true); setError(null);
    const request = invokeCancellable<ObjectReference>('usermanagement', 'resolveSid', { securityIdentifier: sid, directoryScope: scope, connection });
    active.current = request;
    try {
      const reference = await request.promise;
      if (own === generation.current) navigate(objectPath(reference), { state: { directoryEndpoint: { ...endpoint, domain: scope } } });
    } catch (caught) { if (own === generation.current) setError(presentError(caught).message); }
    finally { if (own === generation.current) { setBusy(false); active.current = null; } }
  };
  const returnPath = (location.state as { returnObject?: unknown } | null)?.returnObject;
  return <div className="space-y-4">
    <PageHeader title="Resolve observed directory account" subtitle="One explicit, bounded AD read" />
    <Card title="Stored SID observation">
      <p className="mb-3 break-all font-mono text-sm">{sid || 'No SID supplied'}</p>
      <p className="mb-3 text-sm text-muted">Choose the directory that can authoritatively resolve this SID. Account names and device names do not select an account. A historical observation does not establish current ownership or assignment.</p>
      <label className="text-xs text-muted">Directory DNS scope<Input value={scope} disabled={busy} onChange={event => setScope(event.target.value)} placeholder="example.test" /></label>
      <div className="mt-3 flex gap-3">
        <Button disabled={busy || !scope.trim() || !sid} onClick={() => void resolve()}>Resolve SID and open account</Button>
        {busy && <Button onClick={() => { generation.current++; active.current?.cancel(); active.current = null; setBusy(false); }}>Cancel read</Button>}
      </div>
      {error && <p role="alert" className="mt-3 text-fail-400">{error}</p>}
    </Card>
    {typeof returnPath === 'string' && /^\/(devices|clients)\//.test(returnPath) && <Link className="text-accent-400 underline" to={returnPath}>Return to observed device</Link>}
  </div>;
}
