import { lazy, Suspense, useCallback, useEffect, useRef, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import type { ItLifecycleSettingsResult, OpsiConnectionStatusResult, OpsiSettingsResult, NessusCredentialStatus, ServiceCredentialStatuses } from '../../shared/api-types.generated';
import { invoke } from '../../shared/bridge/bridgeClient';
import { presentError } from '../../shared/bridge/errorPresentation';
import { useEnvironment } from '../../shared/environment/EnvironmentContext';
import { useOptionalWorkingSet } from '../../shared/objects/WorkingSetContext';
import { Button } from '../../shared/ui/Button';
import { Card } from '../../shared/ui/Card';
import { PageHeader } from '../../shared/ui/PageHeader';
import { Spinner } from '../../shared/ui/Spinner';

const ActiveDirectoryPage = lazy(() => import('../activedirectory/ActiveDirectoryPage').then(module => ({ default: module.ActiveDirectoryPage })));
const Microsoft365Page = lazy(() => import('../microsoft365/Microsoft365Page').then(module => ({ default: module.Microsoft365Page })));
function ManagementSourceSessions() {
  const environment = useEnvironment();
  const workspace = useOptionalWorkingSet();
  const [settings, setSettings] = useState<OpsiSettingsResult | null>(null);
  const [opsi, setOpsi] = useState<OpsiConnectionStatusResult | null>(null);
  const [ksc, setKsc] = useState<ItLifecycleSettingsResult | null>(null);
  const [nessus, setNessus] = useState<NessusCredentialStatus | null>(null);
  const [credentials, setCredentials] = useState<ServiceCredentialStatuses | null>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const generation = useRef(0);
  const load = useCallback(async () => {
    const own = ++generation.current;
    setBusy(true);
    const results = await Promise.allSettled([
      invoke<OpsiSettingsResult>('system', 'getOpsiSettings'),
      invoke<OpsiConnectionStatusResult>('patchmanagement', 'getConnectionStatus', { connectStoredCredential: false }),
      invoke<ItLifecycleSettingsResult>('system', 'getItLifecycleSettings'),
      invoke<NessusCredentialStatus>('vulnerabilitymanagement', 'getCredentialStatus', {}),
      invoke<ServiceCredentialStatuses>('system', 'getServiceCredentialStatuses'),
    ]);
    if (generation.current !== own) return;
    setSettings(results[0].status === 'fulfilled' ? results[0].value : null);
    setOpsi(results[1].status === 'fulfilled' ? results[1].value : null);
    setKsc(results[2].status === 'fulfilled' ? results[2].value : null);
    setNessus(results[3].status === 'fulfilled' ? results[3].value : null);
    setCredentials(results[4].status === 'fulfilled' ? results[4].value : null);
    setErrors(results.flatMap((result, index) => result.status === 'rejected'
      ? [`${['opsi configuration', 'opsi session', 'Kaspersky configuration', 'Nessus credential status', 'Stored credential status'][index]}: ${presentError(result.reason).message}`] : []));
    setBusy(false);
  }, []);
  useEffect(() => { void load(); return () => { generation.current++; }; }, [load]);

  const changeOpsi = async (action: 'connect' | 'disconnect') => {
    if (action === 'connect' && !settings) return;
    const own = ++generation.current;
    setBusy(true); setErrors([]); workspace?.clearFamily('management'); environment.invalidate();
    try {
      const value = await invoke<OpsiConnectionStatusResult>('patchmanagement', action, action === 'disconnect' ? {} : {
        server: settings!.settings.server, trustServerCertificate: settings!.settings.trustServerCertificate,
        userName: '', password: null, useStoredCredential: true, rememberCredential: false,
      });
      if (generation.current === own) setOpsi(value);
    } catch (error) { if (generation.current === own) setErrors([presentError(error).message]); }
    finally { if (generation.current === own) { setBusy(false); void workspace?.refreshCached(); } }
  };
  const sourceStates = environment.result?.sources;
  return <div className="space-y-4">
    <div className="flex flex-wrap gap-3"><Button disabled={busy} onClick={() => void load()}>Update connection status</Button>
      <Button disabled={busy || environment.loading} onClick={() => void environment.refresh().then(() => workspace?.refreshCached())}>Read management inventory</Button>
      {environment.loading && <Button onClick={environment.cancel}>Cancel inventory read</Button>}</div>
    <p className="text-xs text-muted">This area controls current sessions, connection status and explicit inventory reads. Saved endpoints, certificate policy, thresholds and credentials are edited only in Settings.</p>
    {busy && <Spinner label="Reading source status…" />}
    {errors.map((error, index) => <p role="alert" key={index} className="text-fail-400">{error}</p>)}
    {environment.error && <p role="alert" className="text-fail-400">{environment.error.message}</p>}
    <Card title="Kaspersky Security Center">
      <p className="text-sm">Server: {ksc?.settings.kaspersky.server || 'Not configured / unavailable'} · Inventory: {sourceStates?.kaspersky.availability ?? 'Not read'}</p>
      {sourceStates?.kaspersky.error && <p role="alert">{sourceStates.kaspersky.error}</p>}
      <p className="my-2 text-xs text-muted">KES is not a separate data source. Its installation and version status are attributes returned by the Kaspersky Security Center inventory.</p>
      <p className="my-2 text-xs text-muted">{credentials?.kaspersky.saved ? 'A KSC account is stored securely and will be used by explicit reads.' : 'No KSC account is stored. Add one in Settings before reading Kaspersky inventory.'}</p>
      <Link className="mt-3 block text-sm text-accent-400 underline" to="/settings?section=environment-health">Manage Kaspersky settings and account</Link>
    </Card>
    <Card title="opsi">
      <p className="text-sm">{opsi ? opsi.connected ? `Connected to ${opsi.serverUrl} as ${opsi.userName}` : 'Not connected' : 'Session status unavailable'} · Inventory: {sourceStates?.opsi.availability ?? 'Not read'}</p>
      {(opsi?.connectionError || sourceStates?.opsi.error) && <p role="alert">{opsi?.connectionError ?? sourceStates?.opsi.error}</p>}
      {opsi?.connected ? <Button disabled={busy} onClick={() => void changeOpsi('disconnect')}>Disconnect opsi</Button> : <div className="my-3 space-y-3">
        <p className="text-xs text-muted">Uses the saved server, certificate policy and account: {settings?.settings.server || 'Not configured / unavailable'}.</p>
        {credentials?.opsi.saved
          ? <Button disabled={busy || !settings?.settings.server} onClick={() => void changeOpsi('connect')}>Connect saved opsi account</Button>
          : <p className="text-xs text-warn-300">No opsi account is stored. Add one in Settings before connecting.</p>}
      </div>}
      <Link className="mt-3 block text-sm text-accent-400 underline" to="/settings?section=patch-management">Manage opsi settings and account</Link>
    </Card>
    <Card title="Nessus">
      <p className="text-sm">{nessus ? nessus.saved ? 'API keys stored securely' : 'No saved API keys' : 'Credential status unavailable'} · Inventory: {sourceStates?.nessus.availability ?? 'Not read'}</p>
      {sourceStates?.nessus.error && <p role="alert">{sourceStates.nessus.error}</p>}
      <div className="mt-3 flex flex-wrap gap-4 text-sm text-accent-400 underline"><Link to="/vulnerabilities">Scan status, findings and explicit synchronization</Link><Link to="/settings?section=vulnerability-management">Manage Nessus settings and API keys</Link></div>
    </Card>
  </div>;
}

export function DataSourcesPage() {
  const [parameters] = useSearchParams();
  const source = parameters.get('source') ?? 'management';
  return <div className="space-y-4">
    <PageHeader title="Data sources" subtitle="Current source sessions, coverage and explicit reads" />
    <nav aria-label="Data sources" className="flex flex-wrap gap-4 text-sm text-accent-400 underline">
      <Link to="/sources?source=management" aria-current={source === 'management' ? 'page' : undefined}>Management connections</Link>
      <Link to="/sources?source=ad" aria-current={source === 'ad' ? 'page' : undefined}>Active Directory analysis</Link>
      <Link to="/sources?source=microsoft365" aria-current={source === 'microsoft365' ? 'page' : undefined}>Microsoft 365</Link>
    </nav>
    <Suspense fallback={<Spinner label="Loading source workspace…" />}>
      {source === 'ad' ? <ActiveDirectoryPage /> : source === 'microsoft365' ? <Microsoft365Page /> : <ManagementSourceSessions />}
    </Suspense>
  </div>;
}
