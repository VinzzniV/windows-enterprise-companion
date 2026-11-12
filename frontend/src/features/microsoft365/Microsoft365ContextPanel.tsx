import { useEffect } from 'react';
import { Link } from 'react-router-dom';
import type { Microsoft365Correlation } from '../../shared/api-types.generated';
import { Button } from '../../shared/ui/Button';
import { Badge } from '../../shared/ui/Badge';
import { Card } from '../../shared/ui/Card';
import { Spinner } from '../../shared/ui/Spinner';
import { CloudLink, CloudUserFields, CloudDeviceFields, CloudManagedFields, timestamp } from './Microsoft365Fields';
import { useMicrosoft365Action } from './useMicrosoft365Action';

export function Microsoft365ContextPanel({ sid, userPrincipalName, host, entraDeviceId }: {
  sid?: string | null; userPrincipalName?: string | null; host?: string; entraDeviceId?: string;
}) {
  const request = useMicrosoft365Action<Microsoft365Correlation>();
  const { run, clear } = request;
  useEffect(() => { clear(); void run('getContext', { sid, userPrincipalName, host, entraDeviceId }); }, [run, clear, sid, userPrincipalName, host, entraDeviceId]);
  const context = request.data;
  return <Card title="Microsoft 365 context">
    <p className="mb-3 text-xs text-muted">Uses already loaded Microsoft Graph evidence only. Opening this section does not scan or query external systems.</p>
    <div className="mb-3 flex flex-wrap gap-3">
      <Link className="text-sm text-accent-400 underline" to="/microsoft365">Open Microsoft 365 sources</Link>
      <Button disabled={request.busy} onClick={() => void run('getContext', { sid, userPrincipalName, host, entraDeviceId })}>Recheck cached evidence</Button>
    </div>
    {request.busy && <Spinner label="Reading cached Microsoft 365 context…" />}
    {request.error && <p role="alert" className="text-sm text-fail-400">{request.error}</p>}
    {context && <div className="flex flex-col gap-3">
      <div><Badge tone={context.state === 'Matched' && !context.stale ? 'accent' : 'warn'}>{context.state}</Badge></div>
      <p className="text-sm">{context.explanation}</p>
      <p className="text-xs text-muted">Microsoft Graph evidence retrieved: {timestamp(context.observedAtUtc)}{context.stale ? ' · stale or missing source' : ''}</p>
      {context.user && <><CloudUserFields user={context.user} /><CloudLink resource="USER" id={context.user.id}>Open Entra user, licenses and groups</CloudLink></>}
      {context.device && <><CloudDeviceFields device={context.device} /><CloudLink resource="DEVICE" id={context.device.id}>Open Entra device</CloudLink></>}
      {context.managedDevice ? <CloudManagedFields device={context.managedDevice} /> : !context.user && <p className="text-sm text-muted">No uniquely correlated Intune record in the loaded cache. Managed/compliant/primary-user status is not established.</p>}
    </div>}
  </Card>;
}
