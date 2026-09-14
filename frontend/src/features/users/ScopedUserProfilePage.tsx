import { useMemo, useState, type ReactNode } from 'react';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import type { CachedMicrosoft365Licenses, Microsoft365ReadState, ObjectReference, ScopedUserProfile } from '../../shared/api-types.generated';
import { objectPath, objectReference, objectSourceLabel } from '../../shared/objects/objectRoutes';
import { ObjectRelationships } from '../../shared/objects/ObjectRelationships';
import { SourceReadState, sourceRetained } from '../../shared/objects/SourceReadState';
import { Button } from '../../shared/ui/Button';
import { Card } from '../../shared/ui/Card';
import { Input } from '../../shared/ui/Input';
import { PageHeader } from '../../shared/ui/PageHeader';
import { Spinner } from '../../shared/ui/Spinner';
import { CloudFields, CloudUserFields, timestamp } from '../microsoft365/Microsoft365Fields';
import { LeaverReviewSection } from './LeaverReviewSection';
import { UserDevicesSection } from './UserDevicesSection';
import { useUserProfile } from './useUserProfile';

const sections = [['overview', 'Identity'], ['access', 'Groups'], ['devices', 'Devices'], ['licenses', 'Licenses'], ['activity', 'Activity'], ['leaver', 'Leaver review']] as const;
type View = ReturnType<typeof useUserProfile>;
function skuLabel(id: string | null, catalogue: CachedMicrosoft365Licenses | undefined) {
  if (!id) return 'SKU identity unavailable';
  const names = [...new Set(catalogue?.licenses.filter(item => item.skuId && item.skuId.toLowerCase() === id.toLowerCase())
    .map(item => item.skuPartNumber).filter((name): name is string => Boolean(name)) ?? [])];
  return names.length === 1 ? names[0] : names.length > 1 ? 'Conflicting product names in source evidence' : 'Product name not resolved';
}
function CloudSource({ title, state, view, children }: { title: string; state: Microsoft365ReadState; view: View; children?: ReactNode }) {
  return <Card title={title}>
    <Button disabled={view.busy || state.availability === 'NOT_ENABLED' || state.availability === 'NOT_CONNECTED'}
      onClick={() => void view.load({ ...state.query, tenantId: state.tenantId ?? undefined })}>Load {title}</Button>
    <SourceReadState state={state} now={view.now} />
    {sourceRetained(state, view.now) && children}
  </Card>;
}

function DirectoryFacts({ profile }: { profile: ScopedUserProfile }) {
  const identity = profile.adProfile?.identity;
  const lifecycle = profile.adProfile?.lifecycle;
  return identity && lifecycle && <Card title="AD identity and lifecycle">
    <CloudFields fields={[
      ['Display name', identity.displayName], ['Account', identity.samAccountName], ['UPN', identity.userPrincipalName], ['Mail', identity.mail],
      ['Employee ID', identity.employeeId], ['Department', identity.department], ['Title', identity.title], ['Manager DN', identity.managerDistinguishedName],
      ['Object GUID', identity.objectId], ['SID', identity.sid], ['Distinguished name', identity.distinguishedName], ['Organizational unit', identity.organizationalUnitPath],
      ['Enabled', lifecycle.enabled], ['Created', timestamp(lifecycle.createdAtUtc)], ['Account expires', timestamp(lifecycle.accountExpiresAtUtc)],
      ['Replicated last logon', timestamp(lifecycle.replicatedLastLogonAtUtc)], ['Password last set', timestamp(lifecycle.passwordLastSetAtUtc)],
      ['Password expires', timestamp(lifecycle.passwordExpiresAtUtc)], ['Password never expires', lifecycle.passwordNeverExpires],
    ]} /><p className="mt-3 text-xs text-muted">AD is authoritative for this AD account. Replicated lastLogonTimestamp can be stale. Manager DN is a source value, not a resolved user relationship.</p>
  </Card>;
}

function Licenses({ read, title, view }: { read: CachedMicrosoft365Licenses; title: string; view: View }) {
  return <CloudSource title={title} state={read.state} view={view}>
    {read.licenses.length === 0 && read.state.availability === 'AVAILABLE' && <p className="text-sm text-muted">No license rows returned by this query; its coverage applies.</p>}
    {read.licenses.map((license, index) => <div key={index} className="my-3 rounded border border-slate-800 p-3">
      <CloudFields fields={[
        ['Product / SKU', license.skuPartNumber], ['SKU ID', license.skuId], ['License record ID', license.id], ['Capability', license.capabilityStatus],
        ['Applies to', license.appliesTo], ['Enabled seats', license.enabledSeats], ['Consumed seats', license.consumedSeats],
      ]} />{license.servicePlans === null ? <p className="text-xs text-muted">Service-plan evidence not available.</p>
        : <ul className="mt-2 space-y-1 text-xs">{license.servicePlans.map((plan, position) => <li key={position}>{plan.name ?? 'Unnamed plan'} · {plan.status ?? 'Status unknown'} · {plan.id ?? 'ID unavailable'}</li>)}</ul>}
    </div>)}
  </CloudSource>;
}

function UserProfileContent({ reference }: { reference: ObjectReference }) {
  const [scopeInput, setScopeInput] = useState('');
  const [directoryScope, setDirectoryScope] = useState<string | null>(null);
  const [groupFilter, setGroupFilter] = useState('');
  const [params, setParams] = useSearchParams();
  const requested = params.get('section');
  const section = sections.some(([key]) => key === requested) ? requested : requested === 'microsoft365' ? 'licenses' : 'overview';
  const view = useUserProfile(reference, directoryScope);
  const profile = view.data;
  const cloud = profile?.cloud;
  const ad = profile?.adProfile;
  const directory = profile?.directory;
  const selectSection = (key: string) => { const next = new URLSearchParams(params); next.set('section', key); setParams(next); };
  const filteredGroups = ad?.access.directGroups.filter(group => `${group.name} ${group.distinguishedName}`.toLocaleLowerCase().includes(groupFilter.trim().toLocaleLowerCase())) ?? [];
  return <div className="flex flex-col gap-4">
    <PageHeader title={profile?.title ?? reference.id} subtitle={`${objectSourceLabel[reference.source]} account · ${reference.scope}`}>
      <Link className="text-sm text-accent-400 underline" to="/users">Users</Link>
      <Button disabled={view.busy} onClick={() => void view.load()}>Reload cached evidence</Button>
      {view.busy && <Button onClick={view.cancel}>Cancel read</Button>}
    </PageHeader>
    {view.error && <p role="alert" className="text-fail-400">{view.error}</p>}
    {view.busy && <Spinner label="Reading available user evidence…" />}
    {profile && <>
      <p className="text-sm text-muted">{profile.identity.replaceAll('_', ' ')} · {profile.explanation}</p>
      {profile.sourceErrors.map((error, index) => <p role="alert" className="text-fail-400" key={index}>{error.message}</p>)}
      <nav aria-label="User profile sections" className="flex flex-wrap gap-2 border-b border-slate-800 pb-3">
        {sections.map(([key, label]) => <Button key={key} variant={section === key ? 'primary' : 'ghost'} aria-current={section === key ? 'page' : undefined} onClick={() => selectSection(key)}>{label}</Button>)}
      </nav>
      <div hidden={section !== 'overview'} className="space-y-4">
        <Card title="Account source references"><CloudFields fields={[["Source", objectSourceLabel[reference.source]], ['Scope', reference.scope], ['Native object ID', reference.id]]} /></Card>
        <Card title="AD source evidence">
          {reference.source === 'ENTRA' && <div className="mb-3 flex flex-wrap items-end gap-2">
            <label className="text-xs text-muted">AD directory DNS scope<Input value={scopeInput} onChange={event => setScopeInput(event.target.value)} placeholder="example.test" /></label>
            <Button disabled={view.busy || !scopeInput.trim()} onClick={() => setDirectoryScope(scopeInput.trim())}>Use this AD scope</Button>
          </div>}
          <Button disabled={view.busy || reference.source === 'ENTRA' && !profile.entraUser?.onPremisesSid}
            onClick={() => void view.load('directory')}>{reference.source === 'ACTIVE_DIRECTORY' ? 'Load this AD user by GUID' : 'Resolve AD account by exact SID'}</Button>
          <p className="my-2 text-xs text-muted">Selected directory: {directory?.data?.directoryScope ?? (reference.source === 'ACTIVE_DIRECTORY' ? reference.scope : directoryScope ?? 'Use the selected directory connection or enter its DNS scope')} · Retrieved: {timestamp(directory?.data?.retrievedAtUtc)} · Last attempt: {timestamp(directory?.lastAttemptAtUtc)}</p>
          {directory?.lastAttemptError && <p role="alert" className="text-fail-400">{directory.lastAttemptError.message}</p>}
          {directory && <p className="text-xs text-muted">{directory.stale || directory.freshUntilUtc && Date.parse(directory.freshUntilUtc) <= view.now ? 'Stale directory evidence' : 'Fresh directory evidence'} · Bounded exact identity query; direct membership does not establish effective access.</p>}
          {!ad && <p className="text-sm text-muted">No AD facts are attached to this profile. An unavailable, unresolved or candidate account is not treated as missing or disabled.</p>}
        </Card>
        <DirectoryFacts profile={profile} />
        {profile.entraUser && <Card title="Entra account identity"><CloudUserFields user={profile.entraUser} /></Card>}
        {cloud?.userReads.map((read, index) => <CloudSource key={index} title={read.state.query.resource === 'USERS_BY_SID' ? 'Entra users with exact SID'
          : read.state.query.resource === 'USER' ? 'Entra user object' : 'bounded Entra user inventory'} state={read.state} view={view} />)}
        <ObjectRelationships title="Account relationships supported by source IDs" links={profile.relationships.filter(link => link.target.kind === 'USER')} />
        <ObjectRelationships title="Separate account candidates and conflicts" links={profile.candidates} />
      </div>
      <div hidden={section !== 'access'} className="space-y-4">
        {ad && <Card title="AD direct groups">
          <p className="mb-3 text-xs text-muted">{ad.access.privilegedCoverageExplanation} Privileged allowlist coverage: {ad.access.privilegedCoverage}. Nested and primary-group membership are not included in memberOf.</p>
          <Input type="search" aria-label="Filter AD direct groups" value={groupFilter} onChange={event => setGroupFilter(event.target.value)} />
          <ul className="mt-3 max-h-96 space-y-2 overflow-y-auto">{filteredGroups.map((group, index) => <li key={index} className="text-sm">{group.name}
            {ad.access.directPrivilegedGroups.some(privileged => privileged.distinguishedName === group.distinguishedName) && <span className="ml-2 text-warn-400">Privileged allowlist</span>}
            <p className="break-all text-xs text-muted">{group.distinguishedName}</p></li>)}</ul>
          {filteredGroups.length === 0 && <p className="mt-3 text-sm text-muted">No direct group matches the current filter in this source evidence.</p>}
        </Card>}
        {cloud?.directGroups && <CloudSource title="Entra direct groups" state={cloud.directGroups.state} view={view} />}
        <ObjectRelationships title="Direct group relationships" links={profile.relationships.filter(link => link.target.kind === 'GROUP')} />
      </div>
      <div hidden={section !== 'devices'} className="space-y-4">
        {ad && <UserDevicesSection profile={ad} />}
        {cloud?.registeredDevices && <CloudSource title="Entra registered devices" state={cloud.registeredDevices.state} view={view} />}
        {cloud?.associatedIntune.map((read, index) => <CloudSource key={index} title={read.state.query.resource === 'MANAGED_DEVICE' ? 'Intune device object' : 'bounded Intune inventory'} state={read.state} view={view} />)}
        <ObjectRelationships title="Device relationships supported by source IDs" links={profile.relationships.filter(link => link.target.kind === 'DEVICE')} />
      </div>
      <div hidden={section !== 'licenses'} className="space-y-4">
        <Card title="Entra assigned SKU evidence">
          {profile.entraUser?.assignedLicenses == null ? <p className="text-sm text-muted">Assigned-license evidence is unavailable.</p>
            : profile.entraUser.assignedLicenses.length === 0 ? <p className="text-sm text-muted">This user object returned an empty assignedLicenses collection.</p>
              : <ul className="space-y-3">{profile.entraUser.assignedLicenses.map((license, index) => <li key={index} className="text-sm">
                SKU {license.skuId ?? 'ID unavailable'} · {skuLabel(license.skuId, cloud?.tenantLicenses)}
                <p className="text-xs text-muted">Disabled plans: {license.disabledPlans?.join(', ') || (license.disabledPlans === null ? 'Unknown' : 'None returned')}</p>
              </li>)}</ul>}
        </Card>
        {cloud?.userLicenses && <Licenses title="User license details" read={cloud.userLicenses} view={view} />}
        {cloud && <Licenses title="Tenant license catalogue" read={cloud.tenantLicenses} view={view} />}
        <p className="text-xs text-muted">User assignments, service-plan provisioning and tenant capacity are separate observations. Cloud data remains in this session and is excluded from exports.</p>
      </div>
      <div hidden={section !== 'activity'} className="space-y-4">
        {cloud?.signIn && <CloudSource title="Entra sign-in evidence" state={cloud.signIn.state} view={view}><CloudFields fields={[
          ['Last sign-in attempt', timestamp(cloud.signIn.activity?.lastSignInAtUtc)], ['Last successful sign-in', timestamp(cloud.signIn.activity?.lastSuccessfulSignInAtUtc)],
        ]} /></CloudSource>}
        {cloud?.registration && <CloudSource title="MFA registration evidence" state={cloud.registration.state} view={view}><CloudFields fields={[
          ['MFA registered', cloud.registration.activity?.mfaRegistered], ['MFA capable', cloud.registration.activity?.mfaCapable], ['Methods registered', cloud.registration.activity?.methodsRegistered?.join(', ')],
        ]} /></CloudSource>}
        <p className="text-xs text-muted">Reports need the existing optional read access. Missing activity is unknown, not proof of inactivity; registration does not establish policy enforcement.</p>
      </div>
      <div hidden={section !== 'leaver'}>{ad ? <LeaverReviewSection profile={ad} subjectKey={`${objectPath(reference)}:${directory?.sessionRevision}`} />
        : <Card title="Leaver review scope"><p className="text-sm text-muted">The existing Leaver assessment requires an explicitly resolved AD account. Cloud-only account evidence is available in this profile; a cloud Leaver workflow and cloud export are not included.</p></Card>}</div>
      <Link className="text-sm text-accent-400 underline" to="/microsoft365">Microsoft 365 connection and source queries</Link>
    </>}
  </div>;
}

export function ScopedUserProfilePage() {
  const { source, scope, objectId } = useParams();
  const reference = useMemo(() => objectReference('USER', source, scope, objectId), [source, scope, objectId]);
  return reference ? <UserProfileContent key={objectPath(reference)} reference={reference} /> : <p role="alert">Invalid user reference. Select an account with a directory or tenant scope.</p>;
}
