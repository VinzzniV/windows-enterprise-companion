import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { Link, useLocation, useParams } from 'react-router-dom';
import type { ManagementDeviceRecordProfile } from '../../shared/api-types.generated';
import { invokeCancellable } from '../../shared/bridge/bridgeClient';
import { presentError } from '../../shared/bridge/errorPresentation';
import { useEnvironmentRequest } from '../../shared/environment/EnvironmentContext';
import { managementRecordPath, managementRecordReference } from '../../shared/objects/managementRecordRoutes';
import { objectPath } from '../../shared/objects/objectRoutes';
import { useOptionalWorkingSet, useWorkingSetSessions } from '../../shared/objects/WorkingSetContext';
import { workingSetSourceLabel } from '../../shared/objects/workingSetSources';
import { Card } from '../../shared/ui/Card';
import { PageHeader } from '../../shared/ui/PageHeader';

function Fact({ label, children }: { label: string; children: ReactNode }) {
  return <div><dt className="text-xs text-muted">{label}</dt><dd className="break-all text-sm">{children ?? 'Unknown / not supplied'}</dd></div>;
}
function time(value: string | null) { return value ? new Date(value).toLocaleString() : null; }

export function ManagementRecordPage() {
  const parameters = useParams();
  const location = useLocation();
  const reference = useMemo(() => managementRecordReference(parameters.source, parameters.workspace, parameters.snapshot, parameters.index),
    [parameters.source, parameters.workspace, parameters.snapshot, parameters.index]);
  const context = useEnvironmentRequest();
  const session = useWorkingSetSessions().management;
  const workspace = useOptionalWorkingSet();
  const [state, setState] = useState<{ reference: typeof reference; context: typeof context; session: number; data: ManagementDeviceRecordProfile | null; error: string | null } | null>(null);
  const generation = useRef(0);
  const current = state?.reference === reference && state.context === context && state.session === session ? state : null;
  useEffect(() => {
    if (!reference) return;
    const own = ++generation.current;
    setState({ reference, context, session, data: null, error: null });
    try {
      const request = invokeCancellable<ManagementDeviceRecordProfile>('clients', 'getManagementRecord', { reference, ...context });
      void request.promise.then(data => { if (own === generation.current) setState({ reference, context, session, data, error: null }); })
        .catch(caught => { if (own === generation.current) setState({ reference, context, session, data: null, error: presentError(caught).message }); });
      return () => { generation.current++; request.cancel(); };
    } catch (caught) { setState({ reference, context, session, data: null, error: presentError(caught).message }); }
  }, [reference, context, session]);
  const data = current?.data;
  const candidates = useMemo(() => {
    if (!data || !reference || !workspace?.displayed) return [];
    const names = new Set([data.record.label, ...data.record.aliases].map(value => value.trim().toLowerCase()).filter(Boolean));
    const own = managementRecordPath(reference);
    return workspace.displayed.objects.filter(object => object.kind === 'DEVICE'
      && !object.observations.some(row => row.managementReference && managementRecordPath(row.managementReference) === own)
      && object.observations.some(row => [row.label, ...row.aliases].some(value => names.has(value.trim().toLowerCase()))));
  }, [data, reference, workspace?.displayed]);
  const from = (location.state as { fromWorkingSet?: string } | null)?.fromWorkingSet;
  const returnTo = from && /^\/devices(?:\?|$)/.test(from) ? from : '/devices';
  const ad = data?.activeDirectory;
  const ksc = data?.kaspersky;
  const opsi = data?.opsi;
  const nessus = data?.nessus;
  return <div className="space-y-4">
    <Link className="text-sm text-accent-400 underline" to={returnTo}>Back to device working set</Link>
    <PageHeader title={data?.record.label ?? 'Management source record'} subtitle={reference ? `${workingSetSourceLabel[reference.source]} · exact snapshot observation` : 'Invalid source reference'} />
    {!reference && <p role="alert">This source-record link is invalid. Select a record from the device working set.</p>}
    {reference && !data && !current?.error && <p role="status">Reading the selected cached record…</p>}
    {current?.error && <p role="alert" className="text-fail-400">{current.error} Return to Devices and load the source again if its snapshot or connection has changed.</p>}
    {data && <>
      <Card title="Identity and source coverage"><p className="mb-3 text-sm">{data.identityExplanation}</p>
        <dl className="grid gap-3 sm:grid-cols-2"><Fact label="Source scope">{data.sourceState.scope}</Fact><Fact label="Source state">{data.sourceState.availability}</Fact>
          <Fact label="Snapshot read">{time(data.retrievedAtUtc)}</Fact><Fact label="Cached source records">{data.sourceState.loadedRecords}</Fact></dl>
        {data.sourceState.error && <p className="mt-3 text-fail-400">{data.sourceState.error}</p>}
        {data.record.nativeReference && <Link className="mt-3 inline-block text-sm text-accent-400 underline" to={objectPath(data.record.nativeReference)}>Inspect native AD object</Link>}
      </Card>
      <Card title="Original source fields"><dl className="grid gap-3 sm:grid-cols-2">
        {ad && <><Fact label="Computer name">{ad.computerName}</Fact><Fact label="DNS host name">{ad.dnsHostName}</Fact><Fact label="Directory scope">{ad.directoryScope}</Fact>
          <Fact label="Object GUID">{ad.objectId}</Fact><Fact label="SID">{ad.securityIdentifier}</Fact><Fact label="Distinguished name">{ad.distinguishedName}</Fact>
          <Fact label="Account state">{ad.enabled === null ? null : ad.enabled ? 'Enabled' : 'Disabled'}</Fact><Fact label="Operating system">{ad.operatingSystem}</Fact>
          <Fact label="Description">{ad.description}</Fact><Fact label="Replicated last logon">{time(ad.lastLogonDate)}</Fact></>}
        {ksc && <><Fact label="Computer name">{ksc.computerName}</Fact><Fact label="FQDN">{ksc.fqdn}</Fact><Fact label="DNS name">{ksc.dnsName}</Fact>
          <Fact label="Native record name">{ksc.recordName}</Fact><Fact label="Last seen">{time(ksc.lastSeen)}</Fact><Fact label="Network agent version">{ksc.agentVersion}</Fact>
          <Fact label="KES version">{ksc.kesVersion}</Fact><Fact label="Administration group">{ksc.administrationGroup}</Fact></>}
        {opsi && <><Fact label="opsi client ID">{opsi.computerName}</Fact><Fact label="Description">{opsi.description}</Fact><Fact label="Depot ID">{opsi.depotId}</Fact>
          <Fact label="Last seen">{time(opsi.lastSeen)}</Fact><Fact label="Client agent version">{opsi.clientAgentVersion}</Fact></>}
        {nessus && <><Fact label="Computer / source address">{nessus.computerName}</Fact><Fact label="FQDN">{nessus.fqdn}</Fact><Fact label="IP address">{nessus.ipAddress}</Fact>
          <Fact label="Stored source key">{nessus.sourceKey}</Fact><Fact label="Legacy asset ID (type unverified)">{nessus.assetId}</Fact>
          <Fact label="Nessus host UUID">{nessus.hostUuid}</Fact><Fact label="BIOS UUID">{nessus.biosUuid}</Fact><Fact label="Last completed scan">{time(nessus.lastCompletedScanUtc)}</Fact>
          <Fact label="Findings by severity">{`Critical ${nessus.critical} · High ${nessus.high} · Medium ${nessus.medium} · Low ${nessus.low} · Info ${nessus.info}`}</Fact>
          <Fact label="Observed ports">{nessus.ports.length ? nessus.ports.join(', ') : 'None in this record'}</Fact><Fact label="Scan sources">{nessus.scanSources.join(', ') || 'Not supplied'}</Fact></>}
      </dl>
        {nessus?.sourceKey && <Link className="mt-3 inline-block text-sm text-accent-400 underline" to={`/vulnerabilities?tab=findings&asset=${encodeURIComponent(nessus.sourceKey)}`}>Inspect stored Nessus findings</Link>}
        {opsi && <Link className="mt-3 inline-block text-sm text-accent-400 underline" to="/patchmanagement">Open opsi package workspace</Link>}
      </Card>
      <Card title="Candidates in the displayed device working set"><p className="mb-3 text-xs text-muted">Matching loaded names or identifiers remain candidates. These links do not establish the same device or authorize an operational action. {candidates.length} loaded matches.</p>
        <ul className="space-y-2 text-sm">{candidates.slice(0, 25).map(candidate => {
          const native = candidate.references[0];
          const record = candidate.observations.find(row => row.managementReference)?.managementReference;
          const to = native ? objectPath(native) : record ? managementRecordPath(record) : `/devices?q=${encodeURIComponent(candidate.label)}`;
          return <li key={candidate.key}><Link className="text-accent-400 underline" to={to}>{candidate.label}</Link>
            <span className="text-xs text-muted"> · {[...new Set(candidate.observations.map(row => workingSetSourceLabel[row.source]))].join(' / ')}</span></li>;
        })}</ul>
        {candidates.length > 25 && <Link className="mt-3 inline-block text-accent-400 underline" to={`/devices?q=${encodeURIComponent(data.record.label)}`}>Inspect matching records in Devices</Link>}
      </Card>
    </>}
  </div>;
}
