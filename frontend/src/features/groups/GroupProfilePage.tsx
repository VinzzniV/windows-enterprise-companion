import { useMemo } from 'react';
import { Link, useLocation, useParams, useSearchParams } from 'react-router-dom';
import type { Microsoft365ReadState, ObjectKind, ObjectReference } from '../../shared/api-types.generated';
import { objectPath, objectReference, objectSourceLabel } from '../../shared/objects/objectRoutes';
import { continueWorkingSetNavigation, workingSetReturn } from '../../shared/objects/workingSetNavigation';
import { SourceReadState, sourceRetained } from '../../shared/objects/SourceReadState';
import { Button } from '../../shared/ui/Button';
import { Card } from '../../shared/ui/Card';
import { DataTable, type DataColumn } from '../../shared/ui/DataTable';
import { PageHeader } from '../../shared/ui/PageHeader';
import { Spinner } from '../../shared/ui/Spinner';
import { Input } from '../../shared/ui/Input';
import { Select } from '../../shared/ui/Select';
import { CloudFields } from '../microsoft365/Microsoft365Fields';
import { DirectoryGroupState } from './DirectoryGroupState';
import { useGroupProfile } from './useGroupProfile';

interface MemberRow {
  key: string;
  index: number;
  source: 'Active Directory' | 'Entra';
  name: string;
  kind: ObjectKind | null;
  typeLabel: string;
  userPrincipalName: string | null;
  accountName: string | null;
  objectId: string | null;
  distinguishedName: string | null;
  reference: ObjectReference | null;
}

const cloudMemberKinds: Record<string, ObjectKind | undefined> = { user: 'USER', group: 'GROUP', device: 'DEVICE' };

function GroupProfileContent({ reference, memberPage }: { reference: ObjectReference; memberPage: number }) {
  const location = useLocation();
  const returnLink = workingSetReturn(location.state, '/groups');
  const [parameters, setParameters] = useSearchParams();
  const memberQuery = parameters.get('memberq') ?? '';
  const loadedMemberPage = Math.max(1, Number(parameters.get('memberpage')) || 1);
  const loadedMemberPageSize = [25, 50, 100].find(value => value === Number(parameters.get('membersize'))) ?? 25;
  const memberSort = ['name', 'type', 'source'].find(value => value === parameters.get('membersort')) ?? 'name';
  const memberDirection = parameters.get('memberdir') === 'desc' ? 'desc' : 'asc';
  const view = useGroupProfile(reference, memberPage);
  const goToPage = async (page: number) => {
    if (await view.load('DIRECTORY_MEMBERS', page)) {
      setParameters(previous => { const next = new URLSearchParams(previous); next.set('page', String(page)); return next; }, { state: location.state });
    }
  };
  const profile = view.data;
  const members = profile?.directoryMembers?.data;
  const navigationState = continueWorkingSetNavigation(location.state);
  const memberRows = useMemo<MemberRow[]>(() => {
    const rows: MemberRow[] = [];
    members?.members.forEach((member, index) => rows.push({ key: `ad:${member.objectId ?? member.distinguishedName}:${index}`, index,
      source: 'Active Directory', name: member.displayName, kind: member.kind, typeLabel: member.objectClass ?? 'Type unavailable',
      userPrincipalName: member.userPrincipalName, accountName: member.samAccountName, objectId: member.objectId,
      distinguishedName: member.distinguishedName, reference: member.objectId && member.kind
        ? { kind: member.kind, source: 'ACTIVE_DIRECTORY', scope: members.directoryScope, id: member.objectId } : null }));
    if (profile?.cloud?.directMembers && sourceRetained(profile.cloud.directMembers.state, view.now)) {
      profile.cloud.directMembers.members.forEach((member, index) => {
        const kind = member.objectType ? cloudMemberKinds[member.objectType.toLowerCase()] ?? null : null;
        rows.push({ key: `entra:${member.id ?? 'unknown'}:${index}`, index: rows.length, source: 'Entra',
          name: member.displayName ?? 'Limited-information object', kind, typeLabel: member.objectType ?? 'Type unavailable',
          userPrincipalName: member.userPrincipalName, accountName: null, objectId: member.id, distinguishedName: null,
          reference: member.id && kind ? { kind, source: 'ENTRA', scope: reference.scope, id: member.id } : null });
      });
    }
    return rows;
  }, [members, profile?.cloud?.directMembers, reference.scope, view.now]);
  const filteredMembers = memberRows.filter(member => `${member.name} ${member.userPrincipalName ?? ''} ${member.accountName ?? ''} ${member.objectId ?? ''} ${member.distinguishedName ?? ''} ${member.typeLabel} ${member.source}`
    .toLocaleLowerCase().includes(memberQuery.trim().toLocaleLowerCase()));
  const sortedMembers = [...filteredMembers].sort((left, right) => {
    const value = memberSort === 'type' ? left.typeLabel.localeCompare(right.typeLabel) : memberSort === 'source' ? left.source.localeCompare(right.source)
      : left.name.localeCompare(right.name);
    return (memberDirection === 'desc' ? -value : value) || left.index - right.index;
  });
  const memberPageCount = Math.max(1, Math.ceil(sortedMembers.length / loadedMemberPageSize));
  const effectiveMemberPage = Math.min(loadedMemberPage, memberPageCount);
  const displayedMembers = sortedMembers.slice((effectiveMemberPage - 1) * loadedMemberPageSize, effectiveMemberPage * loadedMemberPageSize);
  const changeMemberParameter = (name: string, value: string) => setParameters(previous => {
    const next = new URLSearchParams(previous); if (value) next.set(name, value); else next.delete(name);
    if (name !== 'memberpage') next.delete('memberpage'); return next;
  }, { state: location.state });
  const memberColumns: DataColumn<MemberRow>[] = [
    { id: 'name', header: 'Member', sortable: true, cell: member => <div>{member.reference
      ? <Link className="font-medium text-accent-400 underline" to={objectPath(member.reference)} state={navigationState}>{member.name}</Link>
      : <strong>{member.name}</strong>}
      {member.userPrincipalName && <p className="text-xs text-slate-300">{member.userPrincipalName}</p>}
      {member.accountName && member.accountName !== member.name && <p className="text-xs text-muted">{member.accountName}</p>}
    </div> },
    { id: 'type', header: 'Type', sortable: true, cell: member => <span>{member.typeLabel}{!member.reference && <span className="block text-xs text-muted">No supported native profile link</span>}</span> },
    { id: 'source', header: 'Source', sortable: true, cell: member => member.source },
    { header: 'Identity detail', cell: member => <details><summary className="cursor-pointer text-accent-400">ID and directory path</summary>
      <p className="break-all text-xs text-muted">{member.objectId ?? 'ID unavailable'}{member.distinguishedName ? ` · ${member.distinguishedName}` : ''}</p></details> },
  ];
  const refresh = (state: Microsoft365ReadState) => <Button disabled={view.busy || state.availability === 'NOT_CONNECTED' || state.availability === 'NOT_ENABLED'}
    onClick={() => void view.load({ ...state.query, tenantId: state.tenantId ?? undefined })}>Load this source query</Button>;
  return <div className="flex flex-col gap-4">
    <PageHeader title={profile?.title ?? reference.id} subtitle={`${objectSourceLabel[reference.source]} group · ${reference.scope}`}>
      <Link className="text-sm text-accent-400 underline" to={returnLink.to} state={returnLink.state}>← Back to results</Link>
      <Link className="text-sm text-accent-400 underline" to="/groups">All groups</Link>
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
      {profile.directoryMembers && <Card title="AD member source">
        <Button disabled={view.busy} onClick={() => void view.load('DIRECTORY_MEMBERS')}>Load this member page</Button>
        <DirectoryGroupState state={profile.directoryMembers.state} now={view.now} />
        {members && <>
          <p className="mb-3 text-xs text-muted">{members.coverageExplanation}</p>
          <div className="mb-3 flex flex-wrap items-center gap-3 text-sm">
            <Button disabled={view.busy || members.page <= 1} onClick={() => void goToPage(members.page - 1)}>Previous member page</Button>
            <span>Page {members.page} · {members.members.length} displayed · {members.totalCount} visible source matches</span>
            <Button disabled={view.busy || members.page * members.pageSize >= members.totalCount} onClick={() => void goToPage(members.page + 1)}>Next member page</Button>
          </div>
        </>}
      </Card>}
      {profile.cloud?.groupReads.map((read, index) => <Card key={index} title={read.state.query.resource === 'GROUP' ? 'Entra group object' : 'Entra group inventory evidence'}>
        {refresh(read.state)}<SourceReadState state={read.state} now={view.now} />
        {sourceRetained(read.state, view.now) && read.groups.map((group, position) => <CloudFields key={position} fields={[
          ['Display name', group.displayName], ['Object ID', group.id], ['Security enabled', group.securityEnabled], ['Mail enabled', group.mailEnabled],
          ['Group types', group.groupTypes?.join(', ')], ['Dynamic membership rule', group.membershipRule], ['Rule processing state', group.membershipRuleProcessingState], ['Visibility', group.visibility],
        ]} />)}
      </Card>)}
      {profile.cloud?.directMembers && <Card title="Entra member source">
        {refresh(profile.cloud.directMembers.state)}<SourceReadState state={profile.cloud.directMembers.state} now={view.now} />
        <p className="mb-3 text-xs text-muted">Direct Graph members only. Visibility, permission and configured read limits apply. Nested groups are not expanded; unknown object types remain unresolved.</p>
      </Card>}
      {(profile.directoryMembers || profile.cloud?.directMembers) && <Card title="Loaded direct members">
        <p className="mb-3 text-xs text-muted">{memberRows.length} loaded rows · {filteredMembers.length} current matches. AD source paging is controlled above; this table only filters, sorts and pages rows already loaded.</p>
        <div className="mb-3 flex flex-wrap items-end gap-3">
          <label className="text-xs text-muted">Search loaded members<Input value={memberQuery} onChange={event => changeMemberParameter('memberq', event.target.value)} /></label>
          <label className="text-xs text-muted">Sort loaded members<Select value={memberSort} onChange={event => changeMemberParameter('membersort', event.target.value)}>
            <option value="name">Name</option><option value="type">Type</option><option value="source">Source</option>
          </Select></label>
          <Button variant="ghost" onClick={() => changeMemberParameter('memberdir', memberDirection === 'asc' ? 'desc' : 'asc')}>{memberDirection === 'asc' ? 'Ascending' : 'Descending'}</Button>
        </div>
        <DataTable columns={memberColumns} rows={displayedMembers} getRowKey={member => member.key} emptyMessage="No loaded direct members match this search."
          sort={{ column: memberSort, direction: memberDirection }} onSortChange={sort => {
            setParameters(previous => { const next = new URLSearchParams(previous); next.set('membersort', sort.column); next.set('memberdir', sort.direction); next.delete('memberpage'); return next; }, { state: location.state });
          }} pagination={{ page: effectiveMemberPage, pageSize: loadedMemberPageSize, total: filteredMembers.length, itemLabel: 'loaded direct members',
            onPageChange: page => changeMemberParameter('memberpage', String(page)), onPageSizeChange: size => changeMemberParameter('membersize', String(size)), pageSizeOptions: [25, 50, 100] }} />
      </Card>}
      <p className="text-xs text-muted">AD and Entra memberships are separate. Opening a nested group does not traverse a cycle automatically. No cloud data is exported.</p>
    </>}
  </div>;
}
export function GroupProfilePage() {
  const { source, scope, objectId } = useParams();
  const [parameters] = useSearchParams();
  const requestedPage = Number(parameters.get('page'));
  const memberPage = Number.isSafeInteger(requestedPage) && requestedPage > 0 ? requestedPage : 1;
  const reference = useMemo(() => objectReference('GROUP', source, scope, objectId), [source, scope, objectId]);
  return reference ? <GroupProfileContent key={`${objectPath(reference)}:${memberPage}`} reference={reference} memberPage={memberPage} /> : <p role="alert">Invalid scoped group reference.</p>;
}
