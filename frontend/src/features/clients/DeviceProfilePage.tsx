import { useMemo } from 'react';
import { Link, useLocation, useParams } from 'react-router-dom';
import type { DeviceProfileResult, Microsoft365ReadState, ObjectReference, ObjectRelationship } from '../../shared/api-types.generated';
import { objectPath, objectReference, objectSourceLabel } from '../../shared/objects/objectRoutes';
import { SourceReadState, sourceRetained } from '../../shared/objects/SourceReadState';
import { useEnvironment } from '../../shared/environment/EnvironmentContext';
import { Button } from '../../shared/ui/Button';
import { Card } from '../../shared/ui/Card';
import { PageHeader } from '../../shared/ui/PageHeader';
import { Spinner } from '../../shared/ui/Spinner';
import { CloudDeviceFields, CloudFields, CloudManagedFields, timestamp } from '../microsoft365/Microsoft365Fields';
import { StoredClientEvidence } from './sections/OverviewSection';
import { useDeviceProfile } from './useDeviceProfile';

const canRead = (state: Microsoft365ReadState) => state.availability !== 'NOT_CONNECTED' && state.availability !== 'NOT_ENABLED';

function Relations({ title, links }: { title: string; links: ObjectRelationship[] }) {
  return <Card title={title}>{links.length === 0 ? <p className="text-sm text-muted">No relationships established in the loaded evidence.</p>
    : <ul className="space-y-3">{links.map((link, index) => <li key={`${objectPath(link.target)}:${index}`}>
      <Link className="text-accent-400 underline" to={objectPath(link.target)}>{link.label}</Link>
      <p className="text-xs">{link.relation} · {objectSourceLabel[link.target.source]} · {link.target.scope}</p>
      <p className="text-xs text-muted">{link.explanation}</p>
    </li>)}</ul>}</Card>;
}

function ManagementCandidates({ profile }: { profile: DeviceProfileResult }) {
  const source = profile.managementCandidates;
  if (!source) return <p className="text-sm text-muted">Management evidence has not been loaded for this connection context.</p>;
  return <Card title="Management source candidates">
    <p className="text-sm text-warn-400">Address and name evidence only. These records do not establish a shared device identity.</p>
    <p className="my-2 text-xs text-muted">Retrieved: {timestamp(source.retrievedAtUtc)}. The counts below describe each source query, not a combined fleet total.</p>
    <div className="grid gap-3 sm:grid-cols-2">{source.sources.map(state => <div key={state.source} className="text-xs">
      <strong>{state.source}</strong> · {state.availability} · {state.loadedRecords} source records · Scope: {state.scope ?? 'Not provided'}
      {state.error && <p role="alert" className="text-fail-400">{state.error}</p>}
    </div>)}</div>
    {source.kaspersky.map((device, index) => <details key={`ksc:${index}`} className="mt-3 rounded border border-slate-800 p-3">
      <summary>Kaspersky candidate · {device.fqdn ?? device.computerName}</summary><CloudFields fields={[
        ['Computer name', device.computerName], ['FQDN', device.fqdn], ['DNS name', device.dnsName], ['Source record name', device.recordName],
        ['Network Agent version', device.agentVersion], ['KES version', device.kesVersion], ['Administration group', device.administrationGroup], ['Last seen', timestamp(device.lastSeen)],
      ]} /></details>)}
    {source.opsi.map((device, index) => <details key={`opsi:${index}`} className="mt-3 rounded border border-slate-800 p-3">
      <summary>opsi candidate · {device.computerName}</summary><CloudFields fields={[
        ['Computer name', device.computerName], ['Description', device.description], ['Depot', device.depotId], ['Client agent', device.clientAgentVersion], ['Last seen', timestamp(device.lastSeen)],
      ]} /></details>)}
    {source.nessus.map((device, index) => <details key={`nessus:${index}`} className="mt-3 rounded border border-slate-800 p-3">
      <summary>Nessus candidate · {device.fqdn ?? device.computerName}</summary><CloudFields fields={[
        ['Name', device.computerName], ['FQDN', device.fqdn], ['IP', device.ipAddress], ['Source key', device.sourceKey],
        ['Legacy asset identifier (provenance may be unknown)', device.assetId], ['Host UUID', device.hostUuid], ['BIOS UUID', device.biosUuid],
        ['Last completed scan', timestamp(device.lastCompletedScanUtc)], ['Critical', device.critical], ['High', device.high], ['Medium', device.medium], ['Low', device.low],
        ['Informational', device.info], ['Ports', device.ports.join(', ')], ['Scan sources', device.scanSources.join(', ')],
      ]} />{device.sourceKey && <Link className="text-accent-400 underline" to={`/vulnerabilities?asset=${encodeURIComponent(device.sourceKey)}`}>Open source findings</Link>}</details>)}
  </Card>;
}

function DeviceProfileContent({ reference }: { reference: ObjectReference }) {
  const view = useDeviceProfile(reference);
  const environment = useEnvironment();
  const location = useLocation();
  const profile = view.data;
  const directory = profile?.directory;
  const cloud = profile?.cloud;
  return <div className="flex flex-col gap-4">
    <PageHeader title={profile?.title ?? reference.id} subtitle={`${objectSourceLabel[reference.source]} · ${reference.scope}`}>
      <Link className="text-sm text-accent-400 underline" to="/clients">Devices</Link>
      <Button disabled={view.busy} onClick={() => void view.load()}>Reload cached evidence</Button>
      {view.busy && <Button onClick={view.cancel}>Cancel read</Button>}
    </PageHeader>
    {view.error && <p role="alert" className="text-fail-400">{view.error}</p>}
    {view.busy && <Spinner label="Reading available device evidence…" />}
    {profile && <>
      <Card title="Device identity">
        <p className={profile.identity === 'CONFLICT' || profile.identity === 'AMBIGUOUS' ? 'text-warn-400' : 'text-slate-200'}>{profile.explanation}</p>
        <CloudFields fields={[["Source", objectSourceLabel[reference.source]], ['Scope', reference.scope], ['Source object / target', reference.id]]} />
        {profile.sourceErrors.map((error, index) => <p key={index} role="alert" className="mt-2 text-fail-400">{error.message}</p>)}
      </Card>
      {profile.operationalHost && <Card title="Windows tools for this exact target">
        <div className="flex flex-wrap gap-3">{[['inventory', 'Inventory'], ['security', 'Security'], ['diagnostics', 'Health'], ['events', 'Event logs'], ['printers', 'Printers'], ['reporting', 'Report export']].map(([section, label]) =>
          <Link key={section} state={{ returnObject: location.pathname }} className="text-sm text-accent-400 underline"
            to={`/clients/${encodeURIComponent(profile.operationalHost!)}?section=${section}`}>{label}</Link>)}</div>
      </Card>}
      {!profile.operationalHost && <p className="text-sm text-muted">Windows scans and exports are not applicable until an exact Windows target is selected. Candidate links open separate source records.</p>}
      {profile.wec && profile.operationalHost && <StoredClientEvidence host={profile.operationalHost} result={profile.wec} />}
      {reference.source === 'ACTIVE_DIRECTORY' && <Card title="Active Directory computer">
        <Button disabled={view.busy} onClick={() => void view.load('directory')}>Load this AD computer by GUID</Button>
        <p className="my-2 text-xs text-muted">Directory: {reference.scope} · Retrieved: {timestamp(directory?.data?.retrievedAtUtc ?? profile.managementCandidates?.retrievedAtUtc)} · Last attempt: {timestamp(directory?.lastAttemptAtUtc)}</p>
        {directory?.lastAttemptError && <p role="alert" className="text-fail-400">{directory.lastAttemptError.message}</p>}
        {directory?.stale && <p className="text-xs text-warn-400">Previous directory evidence is stale.</p>}
        {profile.directoryRecords.length === 0 && <p className="text-sm text-muted">{directory?.data ? 'No computer returned by this exact source query.' : 'No cached computer identity. Load it explicitly.'}</p>}
        {profile.directoryRecords.map((computer, index) => <CloudFields key={index} fields={[
          ['Name', computer.computerName], ['DNS name', computer.dnsHostName], ['Object GUID', computer.objectId], ['SID', computer.securityIdentifier],
          ['Enabled', computer.enabled], ['OS', computer.operatingSystem], ['Description', computer.description], ['Distinguished name', computer.distinguishedName], ['Replicated last logon', timestamp(computer.lastLogonDate)],
        ]} />)}
      </Card>}
      {cloud?.entraReads.map((read, index) => <Card key={`entra:${index}`} title={read.state.query.resource === 'DEVICE' ? 'Entra object read' : 'Entra inventory evidence'}>
        <Button disabled={view.busy || !canRead(read.state)} onClick={() => void view.load({ ...read.state.query, tenantId: read.state.tenantId ?? undefined })}>Load this Entra source</Button>
        <SourceReadState state={read.state} now={view.now} />
        {sourceRetained(read.state, view.now) && read.devices.map((device, position) => <div key={position} className="my-3 border-t border-slate-800 pt-2">
          <p className="text-xs text-muted">{reference.source === 'ENTRA' && device.id?.toLowerCase() === reference.id.toLowerCase() ? 'Source identity for this profile' : 'Related or candidate source record; see the relationship evidence below'}</p>
          <CloudDeviceFields device={device} />
        </div>)}
      </Card>)}
      {cloud && [cloud.intune, ...cloud.managedDetails].map((read, index) => <Card key={`intune:${index}`} title={read.state.query.resource === 'MANAGED_DEVICE' ? 'Intune object read' : 'Intune inventory evidence'}>
        <Button disabled={view.busy || !canRead(read.state)} onClick={() => void view.load({ ...read.state.query, tenantId: read.state.tenantId ?? undefined })}>{read.state.query.resource === 'MANAGED_DEVICE' ? 'Load this Intune device' : 'Load bounded Intune inventory'}</Button>
        <SourceReadState state={read.state} now={view.now} />
        {sourceRetained(read.state, view.now) && read.devices.map((device, position) => <div key={position} className="my-3 border-t border-slate-800 pt-2"><CloudManagedFields device={device} /></div>)}
      </Card>)}
      {cloud?.registeredOwners && <Card title="Registered owners (Entra)">
        <Button disabled={view.busy || !canRead(cloud.registeredOwners.state)} onClick={() => void view.load({ ...cloud.registeredOwners!.state.query, tenantId: cloud.registeredOwners!.state.tenantId ?? undefined })}>Load registered owners</Button>
        <SourceReadState state={cloud.registeredOwners.state} now={view.now} />
        {sourceRetained(cloud.registeredOwners.state, view.now) && cloud.registeredOwners.members.map((member, index) => <p key={index} className="text-sm">{member.displayName ?? 'Limited-information object'} · {member.objectType ?? 'Type unavailable'} · {member.id ?? 'ID unavailable'}</p>)}
      </Card>}
      <div className="grid gap-4 lg:grid-cols-2"><Relations title="Relationships supported by source IDs" links={profile.relationships} /><Relations title="Candidates requiring selection" links={profile.candidates} /></div>
      <ManagementCandidates profile={profile} />
      <div className="flex flex-wrap items-center gap-3">
        <Button disabled={environment.loading || view.busy} onClick={() => void environment.refresh().then(() => view.load())}>Load management sources: AD, KSC, opsi, Nessus</Button>
        {environment.loading && <Button onClick={environment.cancel}>Cancel management load</Button>}
        <Link className="text-sm text-accent-400 underline" to="/microsoft365">Microsoft 365 connection and source queries</Link>
      </div>
    </>}
  </div>;
}

export function DeviceProfilePage() {
  const { source, scope, objectId } = useParams();
  const reference = useMemo(() => objectReference('DEVICE', source, scope, objectId), [source, scope, objectId]);
  return reference ? <DeviceProfileContent reference={reference} /> : <p role="alert">Invalid device reference. Select a scoped device from the device list.</p>;
}
