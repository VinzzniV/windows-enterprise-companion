import { useCallback, useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import type { Microsoft365Configuration, Microsoft365Connection, Microsoft365Resource, Microsoft365Snapshot, Microsoft365Status } from '../../shared/api-types.generated';
import { Button } from '../../shared/ui/Button';
import { Badge } from '../../shared/ui/Badge';
import { Card } from '../../shared/ui/Card';
import { Input } from '../../shared/ui/Input';
import { PageHeader } from '../../shared/ui/PageHeader';
import { Spinner } from '../../shared/ui/Spinner';
import { cloudPath, timestamp } from './Microsoft365Fields';
import { Microsoft365DataView } from './Microsoft365DataView';
import { Microsoft365ContextPanel } from './Microsoft365ContextPanel';
import { useMicrosoft365Action } from './useMicrosoft365Action';

export const resourceLabels: Record<Microsoft365Resource, string> = {
  TENANT: 'Overview', USERS: 'Users', USER: 'User details', GROUPS: 'Groups', GROUP: 'Group details',
  DEVICES: 'Entra devices', DEVICE: 'Entra device details', MANAGED_DEVICES: 'Intune devices', MANAGED_DEVICE: 'Intune device details', LICENSES: 'Licenses',
  USER_LICENSES: 'User licenses', USER_GROUPS: 'Direct user groups', USER_DEVICES: 'Registered user devices',
  GROUP_MEMBERS: 'Direct group members', DEVICE_OWNERS: 'Registered device owners',
  USER_ACTIVITY: 'Sign-in evidence', USER_REGISTRATION: 'MFA registration',
};
const topResources: Microsoft365Resource[] = ['TENANT', 'USERS', 'LICENSES', 'GROUPS', 'DEVICES', 'MANAGED_DEVICES'];

function QueryPanel({ resource, objectId, onRead }: { resource: Microsoft365Resource; objectId: string | null; onRead(): void }) {
  const query = useMicrosoft365Action<Microsoft365Snapshot>();
  const { run } = query;
  useEffect(() => { void run('read', { resource, objectId }); }, [run, resource, objectId]);
  useEffect(() => { if (query.data || query.error) onRead(); }, [query.data, query.error, onRead]);
  const parent: Microsoft365Resource | null = resource.startsWith('USER_') ? 'USER' : resource === 'GROUP_MEMBERS' ? 'GROUP' : resource === 'DEVICE_OWNERS' ? 'DEVICE' : null;
  const device = query.data?.data?.devices[0];
  return <Card title={resourceLabels[resource]}>
    <div className="mb-3 flex flex-wrap items-center gap-3">
      {parent && objectId && <Link className="text-sm text-accent-400 underline" to={cloudPath(parent, objectId)}>Back to {parent.toLowerCase()}</Link>}
      <Button disabled={query.busy} onClick={() => void run('read', { resource, objectId, refresh: true })}>Refresh this source</Button>
      {query.busy && <><Spinner label="Reading Microsoft Graph…" /><Button onClick={query.cancel}>Cancel</Button></>}
    </div>
    {query.error && <p role="alert" className="mb-3 text-sm text-fail-400">{query.error}{query.data ? ' Previous data remains visible; it has not been refreshed.' : ''}</p>}
    {query.data && <Microsoft365DataView snapshot={query.data} />}
    {resource === 'DEVICE' && device?.deviceId && <div className="mt-4"><Microsoft365ContextPanel entraDeviceId={device.deviceId} /></div>}
  </Card>;
}

export function Microsoft365Page() {
  const [parameters, setParameters] = useSearchParams();
  const requested = parameters.get('resource') ?? 'TENANT';
  const resource: Microsoft365Resource = Object.hasOwn(resourceLabels, requested) ? requested as Microsoft365Resource : 'TENANT';
  const objectId = parameters.get('objectId');
  const status = useMicrosoft365Action<Microsoft365Status>();
  const auth = useMicrosoft365Action<Microsoft365Connection | boolean>();
  const [connection, setConnection] = useState<Microsoft365Connection | null>(null);
  const [configuration, setConfiguration] = useState<Microsoft365Configuration>({ tenantId: '', clientId: '', enableIntune: false, enableAuthenticationReports: false });
  const [sessionRevision, setSessionRevision] = useState(0);
  const { run: readStatus } = status;
  const refreshStatus = useCallback(() => { void readStatus('getStatus'); }, [readStatus]);
  useEffect(() => {
    void readStatus('getStatus').then(value => {
      if (value) { setConnection(value.connection); setConfiguration(value.connection.configuration); }
    });
  }, [readStatus]);

  const connect = async () => {
    setConnection(null);
    const value = await auth.run('connect', configuration);
    if (value && typeof value === 'object') {
      setConnection(value); setSessionRevision(previous => previous + 1);
      await readStatus('getStatus');
    }
  };
  const disconnect = async () => {
    const result = await auth.run('disconnect');
    if (result) {
      setConnection(previous => previous ? { ...previous, connected: false, account: null, permissions: previous.permissions.map(item => ({ ...item, granted: false })) } : null);
      setSessionRevision(previous => previous + 1);
      await readStatus('getStatus');
    }
  };

  return <div className="flex flex-col gap-4">
    <PageHeader title="Microsoft 365" subtitle="Microsoft Graph · read-only administrative evidence">
      <Badge tone={connection?.connected ? 'accent' : 'neutral'}>{connection?.connected ? 'Signed in' : 'Not connected'}</Badge>
    </PageHeader>
    <Card title="Connection">
      <div className="grid gap-3 md:grid-cols-2">
        <label className="text-xs text-muted">Tenant ID<Input value={configuration.tenantId} disabled={auth.busy}
          onChange={event => setConfiguration({ ...configuration, tenantId: event.target.value })} /></label>
        <label className="text-xs text-muted">Client ID<Input value={configuration.clientId} disabled={auth.busy}
          onChange={event => setConfiguration({ ...configuration, clientId: event.target.value })} /></label>
      </div>
      <div className="my-3 flex flex-wrap gap-4 text-sm">
        <label><input type="checkbox" checked={configuration.enableIntune ?? false} disabled={auth.busy}
          onChange={event => setConfiguration({ ...configuration, enableIntune: event.target.checked })} /> Include Intune read access</label>
        <label><input type="checkbox" checked={configuration.enableAuthenticationReports ?? false} disabled={auth.busy}
          onChange={event => setConfiguration({ ...configuration, enableAuthenticationReports: event.target.checked })} /> Include sign-in and MFA-registration reports</label>
      </div>
      <p className="mb-3 text-xs text-muted">Windows Account Manager handles sign-in. No password or client secret is stored by WEC. Changes apply on sign-in; identifiers entered here are session-only. <Link className="text-accent-400 underline" to="/settings?section=microsoft365">Manage saved defaults in Settings</Link>.</p>
      <div className="flex flex-wrap items-center gap-3">
        <Button variant="primary" disabled={auth.busy || status.busy || !configuration.tenantId || !configuration.clientId} onClick={() => void connect()}>Sign in</Button>
        <Button disabled={auth.busy || !connection?.connected} onClick={() => void disconnect()}>Disconnect and clear cache</Button>
        {auth.busy && <><Spinner label="Waiting for Microsoft 365…" /><Button onClick={auth.cancel}>Cancel</Button></>}
        {connection?.account && <span className="text-sm">Connected as {connection.account}</span>}
      </div>
      {status.error && <div className="mt-3"><p role="alert" className="text-fail-400">{status.error}</p>
        <Button onClick={() => void readStatus('getStatus').then(value => { if (value) { setConnection(value.connection); setConfiguration(value.connection.configuration); } })}>Retry connection status</Button></div>}
      {auth.error && <p role="alert" className="mt-3 text-sm text-fail-400">{auth.error}</p>}
      <details className="mt-3 text-xs"><summary className="cursor-pointer text-accent-400">Permissions and tenant setup</summary>
        <p className="my-2 text-muted">Register a single-tenant public client with redirect ms-appx-web://microsoft.aad.brokerplugin/CLIENT-ID. Grant admin consent for selected read scopes. A scope grant does not prove role, license or endpoint access. Optional reports require AuditLog.Read.All; Intune requires DeviceManagementManagedDevices.Read.All.</p>
        <ul className="space-y-1">{connection?.permissions.map(scope => <li key={scope.scope}>{scope.scope} · {scope.granted ? 'Granted in token' : 'Not granted in this WEC session'}</li>)}</ul>
      </details>
    </Card>
    <nav aria-label="Microsoft 365 sections" className="flex flex-wrap gap-2">
      {topResources.map(item => <Button key={item} variant={resource === item ? 'primary' : 'ghost'}
        aria-current={resource === item ? 'page' : undefined} onClick={() => setParameters({ resource: item })}>{resourceLabels[item]}</Button>)}
    </nav>
    {resource === 'TENANT' && connection?.connected && <Card title="Source status">
      <Button disabled={status.busy} onClick={() => void readStatus('getStatus')}>Update displayed cache status</Button>
      <p className="my-2 text-xs text-muted">Source reads are independent. No background tenant-wide synchronization runs. Signed in does not mean Graph is currently reachable.</p>
      <p className="mb-3 text-xs text-muted">Last successful Graph read: {timestamp(status.data?.sources.map(source => source.updatedAtUtc).filter((value): value is string => value !== null).sort().at(-1))}</p>
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">{topResources.filter(item => item !== 'TENANT').map(item => {
        const source = status.data?.sources.find(entry => entry.resource === item);
        return <div key={item} className="rounded border border-slate-800 p-3 text-sm">
          <Link className="text-accent-400 underline" to={cloudPath(item)}>{resourceLabels[item]}</Link>
          <p>{source ? source.loadedCount === null ? 'Read failed; count unavailable' : source.totalCount !== null ? `Graph count: ${source.totalCount}` : `${source.loadedCount} loaded${source.truncated ? ' (partial)' : ''}` : 'Not queried'}</p>
          <p className="text-xs text-muted">Last successful read: {timestamp(source?.updatedAtUtc)}</p>
          {source && <Badge tone={source.stale || source.truncated ? 'warn' : 'neutral'}>{source.truncated ? 'Partial' : source.stale ? 'Stale' : 'Cached'}</Badge>}
          {source?.lastRefreshError && <p role="alert" className="text-fail-400">{source.lastRefreshError.message}</p>}
        </div>;
      })}</div>
    </Card>}
    {connection?.connected ? <QueryPanel key={`${sessionRevision}:${resource}:${objectId ?? ''}`} resource={resource} objectId={objectId} onRead={refreshStatus} />
      : <p className="text-sm text-muted">Sign in to read Microsoft 365. Existing AD and local client views remain available.</p>}
  </div>;
}
