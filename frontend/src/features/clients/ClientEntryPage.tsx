import { useEffect, useState } from 'react';
import { Navigate, useLocation, useParams, useSearchParams } from 'react-router-dom';
import type { StoredObjectLists } from '../../shared/api-types.generated';
import { invokeCancellable } from '../../shared/bridge/bridgeClient';
import { presentError } from '../../shared/bridge/errorPresentation';
import { objectPath } from '../../shared/objects/objectRoutes';
import { Spinner } from '../../shared/ui/Spinner';
import { ClientDetailPage } from './ClientDetailPage';

function ResolveClientAddress({ host }: { host: string }) {
  const location = useLocation();
  const [scope, setScope] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    let current = true;
    const request = invokeCancellable<StoredObjectLists>('clients', 'getStoredObjectLists', { search: host });
    void request.promise.then(result => { if (current) setScope(result.workspace.scope); })
      .catch(caught => { if (current) setError(presentError(caught).message); });
    return () => { current = false; request.cancel(); };
  }, [host]);
  if (error) return <p role="alert" className="text-fail-400">{error}</p>;
  return scope ? <Navigate replace to={objectPath({ kind: 'DEVICE', source: 'WEC', scope, id: host }) + location.search} state={location.state} />
    : <Spinner label="Resolving the exact Windows address…" />;
}

export function ClientEntryPage() {
  const { host } = useParams();
  const [parameters] = useSearchParams();
  if (!host) return <p role="alert">No Windows address was selected.</p>;
  return parameters.get('target') === 'exact' ? <ClientDetailPage /> : <ResolveClientAddress key={host} host={host} />;
}
