import { useEffect, useMemo, useState, type KeyboardEvent } from 'react';
import { Link, useLocation, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import type { DirectoryUserGroup, UserProfileResult } from '../../shared/api-types';
import { invoke } from '../../shared/bridge/bridgeClient';
import { presentError, type ErrorPresentation } from '../../shared/bridge/errorPresentation';
import { useTargets } from '../../shared/targets/TargetContext';
import { Badge } from '../../shared/ui/Badge';
import { Button } from '../../shared/ui/Button';
import { Card } from '../../shared/ui/Card';
import { Input } from '../../shared/ui/Input';
import { PageHeader } from '../../shared/ui/PageHeader';
import { Spinner } from '../../shared/ui/Spinner';
import { EmptyState, ErrorState } from '../../shared/ui/States';
import { loadView } from '../../shared/viewCache';
import {
  emptyUserDirectoryEndpoint,
  formatDirectoryTimestamp,
  toUserDirectoryConnection,
  userDirectoryViewKey,
  type UserDirectoryEndpoint,
} from './users';
import { UserDevicesSection } from './UserDevicesSection';
import { LeaverReviewSection } from './LeaverReviewSection';
import { Microsoft365ContextPanel } from '../microsoft365/Microsoft365ContextPanel';

type UserSection = 'overview' | 'access' | 'devices' | 'leaver' | 'microsoft365';
const sections: { key: UserSection; label: string }[] = [
  { key: 'overview', label: 'Overview' },
  { key: 'access', label: 'Access' },
  { key: 'devices', label: 'Devices' },
  { key: 'leaver', label: 'Leaver review' },
  { key: 'microsoft365', label: 'Microsoft 365' },
];

function isUserSection(value: string | null): value is UserSection {
  return sections.some((section) => section.key === value);
}

function Field({ label, value, mono = false }: { label: string; value: string | null; mono?: boolean }) {
  return <div className="min-w-0 border-b border-slate-800/70 py-2 last:border-b-0">
    <dt className="text-xs font-medium uppercase tracking-wide text-muted">{label}</dt>
    <dd className={`mt-0.5 break-words text-sm text-slate-200 ${mono ? 'font-mono text-xs' : ''}`}>
      {value || 'Not available'}
    </dd>
  </div>;
}

function GroupList({ groups, query }: { groups: DirectoryUserGroup[]; query: string }) {
  const normalized = query.trim().toLocaleLowerCase();
  const filtered = normalized === '' ? groups : groups.filter((group) =>
    `${group.name} ${group.distinguishedName}`.toLocaleLowerCase().includes(normalized));
  if (filtered.length === 0) {
    return <p className="text-sm text-slate-400">No direct group matches this search.</p>;
  }
  return <ul className="max-h-96 divide-y divide-slate-800 overflow-y-auto rounded border border-slate-800">
    {filtered.map((group) => <li key={group.distinguishedName} className="px-3 py-2">
      <div className="text-sm font-medium text-slate-200">{group.name}</div>
      <div className="mt-0.5 break-all font-mono text-xs text-muted">{group.distinguishedName}</div>
    </li>)}
  </ul>;
}

export function UserDetailPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const { objectId: rawObjectId } = useParams<{ objectId: string }>();
  const objectId = decodeURIComponent(rawObjectId ?? '');
  const [searchParams, setSearchParams] = useSearchParams();
  const requestedSection = searchParams.get('section');
  const section: UserSection = isUserSection(requestedSection) ? requestedSection : 'overview';
  const { adminCredentials } = useTargets();
  const endpoint = useMemo(
    () => (location.state as { directoryEndpoint?: UserDirectoryEndpoint } | null)?.directoryEndpoint
      ?? loadView<UserDirectoryEndpoint>(userDirectoryViewKey)
      ?? emptyUserDirectoryEndpoint,
    [location.state],
  );
  const [profile, setProfile] = useState<UserProfileResult | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<ErrorPresentation | null>(null);
  const [revision, setRevision] = useState(0);
  const [groupQuery, setGroupQuery] = useState('');

  useEffect(() => {
    if (objectId === '') {
      setLoading(false);
      return;
    }
    let current = true;
    setLoading(true);
    setError(null);
    void invoke<UserProfileResult>('usermanagement', 'getUserProfile', {
      objectId,
      connection: toUserDirectoryConnection(endpoint, adminCredentials),
    }).then((value) => {
      if (current) setProfile(value);
    }).catch((caught: unknown) => {
      if (current) setError(presentError(caught, {
        message: 'The user profile could not be loaded.',
        action: 'Check the directory connection and retry from the Users workspace.',
      }));
    }).finally(() => {
      if (current) setLoading(false);
    });
    return () => { current = false; };
  }, [adminCredentials, endpoint, objectId, revision]);

  const selectSection = (next: UserSection) => {
    const nextParams = new URLSearchParams(searchParams);
    if (next === 'overview') nextParams.delete('section');
    else nextParams.set('section', next);
    setSearchParams(nextParams);
  };

  const onTabKeyDown = (event: KeyboardEvent<HTMLButtonElement>, index: number) => {
    if (event.key !== 'ArrowRight' && event.key !== 'ArrowLeft') return;
    event.preventDefault();
    const delta = event.key === 'ArrowRight' ? 1 : -1;
    const next = sections[(index + delta + sections.length) % sections.length];
    selectSection(next.key);
    document.getElementById(`usertab-${next.key}`)?.focus();
  };

  if (objectId === '') {
    return <EmptyState title="No user selected" message="Pick a user from the Users workspace." />;
  }

  if (loading && profile === null) return <Spinner label="Loading user profile…" />;
  if (error) return <ErrorState
    title="User profile unavailable"
    {...error}
    controls={<>
      <Button onClick={() => setRevision((value) => value + 1)}>Retry</Button>
      <Button variant="ghost" onClick={() => navigate('/users')}>Back to Users</Button>
    </>}
  />;
  if (!profile) return <EmptyState title="User not found" message="The selected directory identity is no longer available." />;

  const { identity, lifecycle, access } = profile;
  const stateLabel = lifecycle.enabled === true ? 'Enabled' : lifecycle.enabled === false ? 'Disabled' : 'Unknown state';
  const stateTone = lifecycle.enabled === true ? 'ok' : lifecycle.enabled === false ? 'neutral' : 'warn';
  const privilegedAvailable = access.privilegedCoverage === 'AVAILABLE';

  return <div className="flex flex-col gap-4">
    <PageHeader title={identity.displayName} subtitle="User 360 · AD-authoritative read-only profile">
      <Badge tone={stateTone}>{stateLabel}</Badge>
      <Button variant="ghost" onClick={() => selectSection('leaver')}>Start Leaver review</Button>
      <Button variant="ghost" onClick={() => navigate('/users')}>← All users</Button>
    </PageHeader>

    <div role="tablist" aria-label="User sections" className="flex gap-1 border-b border-slate-800">
      {sections.map((entry, index) => <button
        key={entry.key}
        id={`usertab-${entry.key}`}
        role="tab"
        aria-selected={section === entry.key}
        aria-controls={`userpanel-${entry.key}`}
        tabIndex={section === entry.key ? 0 : -1}
        type="button"
        onClick={() => selectSection(entry.key)}
        onKeyDown={(event) => onTabKeyDown(event, index)}
        className={`-mb-px border-b-2 px-3 py-2 text-sm transition-colors ${section === entry.key
          ? 'border-accent-400 font-medium text-white'
          : 'border-transparent text-slate-400 hover:text-slate-200'}`}
      >{entry.label}</button>)}
    </div>

    <div role="tabpanel" id="userpanel-overview" aria-labelledby="usertab-overview" hidden={section !== 'overview'}>
      <div className="grid gap-4 lg:grid-cols-2">
        <Card title="Identity">
          <dl>
            <Field label="Account" value={identity.samAccountName} mono />
            <Field label="User principal name" value={identity.userPrincipalName} />
            <Field label="Mail" value={identity.mail} />
            <Field label="Employee ID" value={identity.employeeId} mono />
            <Field label="Department" value={identity.department} />
            <Field label="Title" value={identity.title} />
            <Field label="Manager DN" value={identity.managerDistinguishedName} mono />
          </dl>
        </Card>
        <Card title="Lifecycle evidence">
          <dl>
            <Field label="Created" value={formatDirectoryTimestamp(lifecycle.createdAtUtc)} />
            <Field label="Account expires" value={formatDirectoryTimestamp(lifecycle.accountExpiresAtUtc)} />
            <Field label="Replicated last logon" value={formatDirectoryTimestamp(lifecycle.replicatedLastLogonAtUtc)} />
            <Field label="Password last set" value={formatDirectoryTimestamp(lifecycle.passwordLastSetAtUtc)} />
            <Field label="Password expires" value={formatDirectoryTimestamp(lifecycle.passwordExpiresAtUtc)} />
            <Field label="Password never expires" value={lifecycle.passwordNeverExpires === null
              ? null
              : lifecycle.passwordNeverExpires ? 'Yes' : 'No'} />
          </dl>
          <p className="mt-3 border-t border-slate-800 pt-3 text-xs text-muted">
            AD lastLogonTimestamp is replicated and can be stale. Missing timestamps are not interpreted as healthy or inactive.
          </p>
        </Card>
        <Card title="Directory location">
          <dl>
            <Field label="Object GUID" value={identity.objectId} mono />
            <Field label="SID" value={identity.sid} mono />
            <Field label="Organizational unit" value={identity.organizationalUnitPath} mono />
            <Field label="Distinguished name" value={identity.distinguishedName} mono />
          </dl>
        </Card>
        <Card title="Device context">
          <div className="flex flex-wrap items-center gap-2">
            <span className="font-mono text-2xl font-semibold text-slate-100">{profile.devices.totalLinkedDeviceCount}</span>
            <span className="text-sm text-slate-300">SID-matched linked devices</span>
          </div>
          <p className="mt-2 text-sm text-slate-300">{profile.devices.explanation}</p>
          <p className="mt-2 text-xs text-muted">
            WEC does not infer ownership from names or profiles. Relationships appear only after an explicit Inventory scan records approved evidence.
          </p>
          <Link className="mt-3 inline-block text-sm text-accent-300 hover:text-accent-200" to="?section=devices">Open device relationships →</Link>
        </Card>
      </div>
    </div>

    <div role="tabpanel" id="userpanel-access" aria-labelledby="usertab-access" hidden={section !== 'access'}>
      <div className="flex flex-col gap-4">
        <section className={`rounded-lg border p-4 ${!privilegedAvailable
          ? 'border-warn-800 bg-warn-950/20'
          : access.directPrivilegedGroups.length > 0
            ? 'border-fail-800 bg-fail-950/20'
            : 'border-ok-900 bg-ok-950/20'}`}>
          <div className="flex flex-wrap items-center gap-2">
            <h2 className="text-sm font-semibold text-slate-100">Privileged direct membership</h2>
            {!privilegedAvailable
              ? <Badge tone="warn">Coverage unavailable</Badge>
              : access.directPrivilegedGroups.length > 0
                ? <Badge tone="fail">{access.directPrivilegedGroups.length} privileged</Badge>
                : <Badge tone="ok">None found</Badge>}
          </div>
          <p className="mt-2 text-xs text-slate-400">{access.privilegedCoverageExplanation}</p>
          {access.directPrivilegedGroups.length > 0 && <div className="mt-3">
            <GroupList groups={access.directPrivilegedGroups} query="" />
          </div>}
        </section>

        <Card title={`Direct groups · ${access.directGroups.length}`}>
          <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
            <Input
              type="search"
              aria-label="Search direct groups"
              placeholder="Search group name or distinguished name"
              value={groupQuery}
              onChange={(event) => setGroupQuery(event.target.value)}
              className="max-w-xl"
            />
            <Link className="text-sm text-accent-300 hover:text-accent-200" to="/activedirectory">
              Open Active Directory →
            </Link>
          </div>
          {access.directGroups.length === 0
            ? <p className="text-sm text-slate-400">No direct group memberships were returned.</p>
            : <GroupList groups={access.directGroups} query={groupQuery} />}
        </Card>
      </div>
    </div>

    <div role="tabpanel" id="userpanel-devices" aria-labelledby="usertab-devices" hidden={section !== 'devices'}>
      <UserDevicesSection profile={profile} />
    </div>

    <div role="tabpanel" id="userpanel-leaver" aria-labelledby="usertab-leaver" hidden={section !== 'leaver'}>
      <LeaverReviewSection profile={profile} />
    </div>
    {section === 'microsoft365' && <div role="tabpanel" id="userpanel-microsoft365" aria-labelledby="usertab-microsoft365">
      <Microsoft365ContextPanel sid={identity.sid} userPrincipalName={identity.userPrincipalName} />
    </div>}
  </div>;
}
