import { useCallback, useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import type { Microsoft365Snapshot } from '../../shared/api-types.generated';
import { useOptionalWorkingSet, useWorkingSetSessions } from '../../shared/objects/WorkingSetContext';
import { SourceReadState, sourceRetained } from '../../shared/objects/SourceReadState';
import { Button } from '../../shared/ui/Button';
import { Input } from '../../shared/ui/Input';
import { useMicrosoft365Action } from './useMicrosoft365Action';
import { Microsoft365DataView } from './Microsoft365DataView';

function LicenseSession() {
  const query = useMicrosoft365Action<Microsoft365Snapshot>();
  const { run } = query;
  const workspace = useOptionalWorkingSet();
  const refreshCached = workspace?.refreshCached;
  const [parameters, setParameters] = useSearchParams();
  const tenantId = parameters.get('tenant');
  const sku = parameters.get('sku') ?? '';
  const [now, setNow] = useState(Date.now);
  const load = useCallback(async (refresh = false) => {
    await run('read', { resource: 'LICENSES', tenantId, refresh, cacheOnly: !refresh });
    if (refresh) void refreshCached?.();
  }, [run, tenantId, refreshCached]);
  useEffect(() => { void load(); }, [load]);
  useEffect(() => { const timer = window.setInterval(() => setNow(Date.now()), 1000); return () => window.clearInterval(timer); }, []);
  const snapshot = query.data;
  const state = snapshot?.state;
  const retained = state && sourceRetained(state, now);
  const displayed = snapshot?.data && retained ? { ...snapshot, data: { ...snapshot.data,
    licenses: snapshot.data.licenses.filter(license => !sku || license.skuId?.toLowerCase() === sku.trim().toLowerCase()) } } : null;
  return <div className="space-y-4">
    <p className="text-sm text-muted">Tenant license capacity and service plans. User assignments remain in account profiles; no common package/license identity is inferred.</p>
    <div className="flex flex-wrap items-end gap-3">
      <Button disabled={query.busy || state?.availability === 'NOT_CONNECTED'} onClick={() => void load(true)}>Load tenant licenses</Button>
      {query.busy && <Button onClick={query.cancel}>Cancel read</Button>}
      <Link className="text-sm text-accent-400 underline" to="/sources?source=microsoft365">Microsoft 365 connection</Link>
      <label className="text-xs text-muted">Selected SKU ID<Input value={sku} onChange={event => setParameters(previous => {
        const next = new URLSearchParams(previous); if (event.target.value) next.set('sku', event.target.value); else next.delete('sku'); return next;
      })} /></label>
    </div>
    {query.error && <p role="alert" className="text-fail-400">{query.error}</p>}
    {state && <SourceReadState state={state} now={now} />}
    {displayed ? <Microsoft365DataView snapshot={displayed} /> : <p className="text-sm text-muted">No retained license catalogue is available for this session. Load it explicitly.</p>}
    {sku && state?.tenantId && <p className="text-sm"><Link className="text-accent-400 underline" to={`/users/workspace?source=ENTRA&sku=${encodeURIComponent(sku)}&tenant=${encodeURIComponent(state.tenantId)}`}>Review loaded user assignments for this SKU</Link>
      <span className="block text-xs text-muted">Only assignment evidence already in the bounded working set is filtered. This is not a complete tenant assignee list.</span></p>}
  </div>;
}

export function LicenseWorkspace() {
  const sessions = useWorkingSetSessions();
  const [parameters] = useSearchParams();
  return <LicenseSession key={`${sessions.cloud}:${parameters.get('tenant') ?? ''}`} />;
}
