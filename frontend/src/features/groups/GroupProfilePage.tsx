import { useMemo } from 'react';
import { Link, useParams } from 'react-router-dom';
import type { Microsoft365ReadState, ObjectReference } from '../../shared/api-types.generated';
import { ObjectRelationships } from '../../shared/objects/ObjectRelationships';
import { objectPath, objectReference, objectSourceLabel } from '../../shared/objects/objectRoutes';
import { SourceReadState, sourceRetained } from '../../shared/objects/SourceReadState';
import { Button } from '../../shared/ui/Button';
import { Card } from '../../shared/ui/Card';
import { PageHeader } from '../../shared/ui/PageHeader';
import { Spinner } from '../../shared/ui/Spinner';
import { CloudFields } from '../microsoft365/Microsoft365Fields';
import { DirectoryGroupState } from './DirectoryGroupState';
import { useGroupProfile } from './useGroupProfile';

function GroupProfileContent({ reference }: { reference: ObjectReference }) {
  const view = useGroupProfile(reference);
  const profile = view.data;
  const members = profile?.directoryMembers?.data;
  const refresh = (state: Microsoft365ReadState) => <Button disabled={view.busy || state.availability === 'NOT_CONNECTED' || state.availability === 'NOT_ENABLED'}
    onClick={() => void view.load({ ...state.query, tenantId: state.tenantId ?? undefined })}>Load this source query</Button>;
  return <div className="flex flex-col gap-4">
    <PageHeader title={profile?.title ?? reference.id} subtitle={`${objectSourceLabel[reference.source]} group · ${reference.scope}`}>
      <Link className="text-sm text-accent-400 underline" to="/groups">Groups</Link>
      <Button disabled={view.busy} onClick={() => void view.load()}>Reload cached evidence</Button>
      {view.busy && <Button onClick={view.cancel}>Cancel read</Button>}
    </PageHeader>
    {view.busy && <Spinner label="Reading available group evidence…" />}
    {view.error && <p role="alert" className="text-fail-400">{view.error}</p>}
    {profile && <>
      <Card title="Group identity"><p className="text-sm">{profile.identity.replaceAll('_', ' ')} · {profile.explanation}</p>
        <CloudFields fields={[["Source", objectSourceLabel[reference.source]], ['Scope', reference.scope], ['Object GUID / ID', reference.id]]} />
        {profile.sourceErrors.map((error, index) => <p role="alert" className="mt-2 text-fail-400" key={index}>{error.message}</p>)}
      </Card>
      {profile.directory && <Card title="AD group source">
        <Button disabled={view.busy} onClick={() => void view.load('DIRECTORY_IDENTITY')}>Load this AD group by GUID</Button>
        <DirectoryGroupState state={profile.directory.state} now={view.now} />
        {profile.directory.data?.groups.map((group, index) => <CloudFields key={index} fields={[
          ['Name', group.name], ['Account', group.samAccountName], ['Object GUID', group.objectId], ['SID', group.securityIdentifier],
          ['Distinguished name', group.distinguishedName], ['Description', group.description], ['Security enabled', group.securityEnabled], ['Group scope', group.groupScope],
        ]} />)}
        {profile.directory.data?.truncated && <p className="text-warn-400">The identity query was truncated. No record was selected automatically.</p>}
      </Card>}
      {profile.directoryMembers && <Card title="AD direct members">
        <Button disabled={view.busy} onClick={() => void view.load('DIRECTORY_MEMBERS')}>Load this member page</Button>
        <DirectoryGroupState state={profile.directoryMembers.state} now={view.now} />
        {members && <>
          <p className="mb-3 text-xs text-muted">{members.coverageExplanation}</p>
          <div className="mb-3 flex flex-wrap items-center gap-3 text-sm">
            <Button disabled={view.busy || members.page <= 1} onClick={() => void view.load('DIRECTORY_MEMBERS', members.page - 1)}>Previous member page</Button>
            <span>Page {members.page} · {members.members.length} displayed · {members.totalCount} visible source matches</span>
            <Button disabled={view.busy || members.page * members.pageSize >= members.totalCount} onClick={() => void view.load('DIRECTORY_MEMBERS', members.page + 1)}>Next member page</Button>
          </div>
          <ul className="space-y-2">{members.members.map((member, index) => <li key={index} className="rounded border border-slate-800 p-3 text-sm">
            {member.displayName} · {member.objectClass ?? 'Type unavailable'}
            <p className="break-all text-xs text-muted">{member.objectId ?? 'GUID unavailable'} · {member.securityIdentifier ?? 'SID unavailable'} · {member.distinguishedName}</p>
            {(!member.objectId || !member.kind) && <p className="text-xs text-muted">No supported native profile link can be established.</p>}
          </li>)}</ul>
        </>}
      </Card>}
      {profile.cloud?.groupReads.map((read, index) => <Card key={index} title={read.state.query.resource === 'GROUP' ? 'Entra group object' : 'Entra group inventory evidence'}>
        {refresh(read.state)}<SourceReadState state={read.state} now={view.now} />
        {sourceRetained(read.state, view.now) && read.groups.map((group, position) => <CloudFields key={position} fields={[
          ['Display name', group.displayName], ['Object ID', group.id], ['Security enabled', group.securityEnabled], ['Mail enabled', group.mailEnabled],
          ['Group types', group.groupTypes?.join(', ')], ['Dynamic membership rule', group.membershipRule], ['Rule processing state', group.membershipRuleProcessingState], ['Visibility', group.visibility],
        ]} />)}
      </Card>)}
      {profile.cloud?.directMembers && <Card title="Entra direct members">
        {refresh(profile.cloud.directMembers.state)}<SourceReadState state={profile.cloud.directMembers.state} now={view.now} />
        <p className="mb-3 text-xs text-muted">Direct Graph members only. Visibility, permission and configured read limits apply. Nested groups are not expanded; unknown object types remain unresolved.</p>
        {sourceRetained(profile.cloud.directMembers.state, view.now) && <ul className="space-y-2">{profile.cloud.directMembers.members.map((member, index) => <li key={index} className="rounded border border-slate-800 p-3 text-sm">
          {member.displayName ?? 'Limited-information object'} · {member.objectType ?? 'Type unavailable'}
          <p className="break-all text-xs text-muted">{member.id ?? 'ID unavailable'} · {member.userPrincipalName ?? 'UPN unavailable'}</p>
        </li>)}</ul>}
      </Card>}
      <ObjectRelationships title="Direct member profiles" links={profile.relationships} />
      <p className="text-xs text-muted">AD and Entra memberships are separate. Opening a nested group does not traverse a cycle automatically. No cloud data is exported.</p>
    </>}
  </div>;
}
export function GroupProfilePage() {
  const { source, scope, objectId } = useParams();
  const reference = useMemo(() => objectReference('GROUP', source, scope, objectId), [source, scope, objectId]);
  return reference ? <GroupProfileContent key={objectPath(reference)} reference={reference} /> : <p role="alert">Invalid scoped group reference.</p>;
}
