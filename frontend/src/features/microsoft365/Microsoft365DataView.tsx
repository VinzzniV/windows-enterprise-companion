import { useState, type ReactNode } from 'react';
import type { Microsoft365Snapshot, Microsoft365Resource, Microsoft365ServicePlan } from '../../shared/api-types.generated';
import { DataTable, type DataColumn } from '../../shared/ui/DataTable';
import { Input } from '../../shared/ui/Input';
import { Badge } from '../../shared/ui/Badge';
import { Select } from '../../shared/ui/Select';
import { available, timestamp, CloudFields, CloudLink, CloudUserFields, CloudDeviceFields, CloudManagedFields } from './Microsoft365Fields';

export function CloudTable<T>({ rows, columns, searchText }: { rows: readonly T[]; columns: DataColumn<T>[]; searchText(row: T): string }) {
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(0);
  const [descending, setDescending] = useState(false);
  const filtered = rows.filter(row => searchText(row).toLocaleLowerCase().includes(search.toLocaleLowerCase()))
    .sort((left, right) => searchText(left).localeCompare(searchText(right)) * (descending ? -1 : 1));
  const actualPage = Math.min(page, Math.max(0, Math.ceil(filtered.length / 50) - 1));
  return <div className="flex flex-col gap-3">
    <label className="text-xs text-muted">Search loaded data
      <Input value={search} onChange={event => { setSearch(event.target.value); setPage(0); }} placeholder="Filter this loaded collection" />
    </label>
    <label className="text-xs text-muted">Sort loaded data
      <Select value={descending ? 'desc' : 'asc'} onChange={event => { setDescending(event.target.value === 'desc'); setPage(0); }}>
        <option value="asc">Name / identifier A–Z</option><option value="desc">Name / identifier Z–A</option>
      </Select>
    </label>
    <DataTable rows={filtered.slice(actualPage * 50, (actualPage + 1) * 50)} columns={columns}
      sort={null} onSortChange={() => undefined}
      emptyMessage="No matching records in the loaded data."
      pagination={{ page: actualPage + 1, pageSize: 50, total: filtered.length, onPageChange: next => setPage(next - 1) }} />
  </div>;
}

function Plans({ plans }: { plans: Microsoft365ServicePlan[] | null }) {
  if (plans === null) return <span>Service plans not available</span>;
  return <details><summary className="cursor-pointer text-accent-400">{plans.length} service plans</summary>
    <ul className="mt-2 max-h-72 space-y-2 overflow-auto">{plans.map((plan, index) => <li key={plan.id ?? index}>
      <span>{available(plan.name)} · {available(plan.status)}</span>
      {plan.status?.toLowerCase() === 'disabled' && <Badge tone="warn">Disabled</Badge>}
      <div className="break-all font-mono text-xs text-muted">{available(plan.id)}</div>
    </li>)}</ul>
  </details>;
}

export function Microsoft365DataView({ snapshot }: { snapshot: Microsoft365Snapshot }) {
  const data = snapshot.data;
  const resource = snapshot.query.resource;
  if (!data) return <p>No source data is available.</p>;
  let content: ReactNode;
  if (resource === 'TENANT') {
    content = data.tenants.length ? data.tenants.map((tenant, i) => <CloudFields key={tenant.id ?? i}
      fields={[["Tenant name", tenant.displayName], ["Tenant ID", tenant.id]]} />) : <p>No tenant information was returned.</p>;
  } else if (resource === 'USER_ACTIVITY' || resource === 'USER_REGISTRATION') {
    content = <><CloudFields fields={resource === 'USER_ACTIVITY' ? [
      ['Last sign-in attempt', timestamp(data.activity?.lastSignInAtUtc)],
      ['Last successful sign-in', timestamp(data.activity?.lastSuccessfulSignInAtUtc)],
    ] : [
      ['MFA registered', data.activity?.mfaRegistered], ['MFA capable', data.activity?.mfaCapable],
      ['Registered method categories', data.activity?.methodsRegistered?.join(', ')],
    ]} /><p className="mt-3 text-xs text-muted">Reports can lag and require tenant roles and licensing. Registration is not MFA enforcement. Missing timestamps do not mean the account never signed in. Registration reports are unavailable for disabled users.</p></>;
  } else if (resource === 'USERS' || resource === 'USER') {
    content = resource === 'USER' && data.users[0] ? <>
      <CloudUserFields user={data.users[0]} />
      <div className="my-3 flex flex-wrap gap-4">{([
        ['USER_LICENSES', 'Licenses and service plans'], ['USER_GROUPS', 'Direct groups'], ['USER_DEVICES', 'Registered devices'],
        ['USER_ACTIVITY', 'Sign-in evidence'], ['USER_REGISTRATION', 'MFA registration'],
      ] as [Microsoft365Resource, string][]).map(([target, label]) => <CloudLink key={target} resource={target} id={data.users[0].id}>{label}</CloudLink>)}</div>
      <p className="text-xs text-muted">Mail is an address, not confirmation of an Exchange mailbox. Registration and group membership are separate from device ownership.</p>
      {data.users[0].assignedLicenses === null ? <p>License assignments not available.</p> : data.users[0].assignedLicenses.length === 0
        ? <Badge tone="warn">No assigned license</Badge> : <ul className="mt-3 space-y-2">{data.users[0].assignedLicenses.map((license, i) => <li key={license.skuId ?? i}>
          SKU {available(license.skuId)} · {license.disabledPlans === null ? 'Disabled plan information not available' : `${license.disabledPlans.length} disabled plans`}
          {license.disabledPlans && license.disabledPlans.length > 0 && <div className="break-all text-xs text-warn-400">{license.disabledPlans.join(', ')}</div>}
        </li>)}</ul>}
    </> : <CloudTable rows={data.users} searchText={row => `${row.displayName ?? ''} ${row.userPrincipalName ?? ''} ${row.mail ?? ''} ${row.department ?? ''}`}
      columns={[
        { header: 'User', cell: row => <CloudLink resource="USER" id={row.id}>{available(row.displayName)}</CloudLink> },
        { header: 'UPN', cell: row => available(row.userPrincipalName) },
        { header: 'Enabled', cell: row => available(row.accountEnabled) },
        { header: 'Department', cell: row => available(row.department) },
        { header: 'Licenses', cell: row => row.assignedLicenses === null ? 'Not available' : row.assignedLicenses.length === 0
          ? <Badge tone="warn">None</Badge> : `${row.assignedLicenses.length}${row.assignedLicenses.length > 1 ? ' (multiple)' : ''}` },
      ]} />;
  } else if (resource === 'GROUPS' || resource === 'GROUP' || resource === 'USER_GROUPS') {
    content = <>
      {resource === 'GROUP' && data.groups[0] ? <><CloudFields fields={[
        ['Name', data.groups[0].displayName], ['Object ID', data.groups[0].id],
        ['Security enabled', data.groups[0].securityEnabled], ['Mail enabled', data.groups[0].mailEnabled],
        ['Group types', data.groups[0].groupTypes?.join(', ') || (data.groups[0].groupTypes ? 'Assigned membership' : null)],
        ['Dynamic rule', data.groups[0].membershipRule], ['Rule processing', data.groups[0].membershipRuleProcessingState],
        ['Visibility', data.groups[0].visibility],
      ]} /><div className="my-3"><CloudLink resource="GROUP_MEMBERS" id={data.groups[0].id}>Open direct members and loaded member count</CloudLink></div></>
        : <CloudTable rows={data.groups} searchText={row => `${row.displayName ?? ''} ${row.id ?? ''}`}
          columns={[
            { header: 'Group', cell: row => <CloudLink resource="GROUP" id={row.id}>{available(row.displayName)}</CloudLink> },
            { header: 'Object ID', cell: row => available(row.id), mono: true },
            { header: 'Type', cell: row => row.groupTypes?.includes('Unified') ? 'Microsoft 365' : row.securityEnabled === true ? 'Security' : row.mailEnabled === true ? 'Distribution' : 'Not available' },
            { header: 'Security / mail', cell: row => `${available(row.securityEnabled)} / ${available(row.mailEnabled)}` },
            { header: 'Membership', cell: row => row.groupTypes === null ? 'Not available' : row.groupTypes.includes('DynamicMembership') ? 'Dynamic' : 'Assigned' },
          ]} />}
      <p className="mt-3 text-xs text-muted">Direct memberships only. Hidden memberships require additional permissions and are outside this read profile. Exchange dynamic distribution groups are not returned by Graph.</p>
    </>;
  } else if (resource === 'DEVICES' || resource === 'DEVICE' || resource === 'USER_DEVICES') {
    content = resource === 'DEVICE' && data.devices[0] ? <><CloudDeviceFields device={data.devices[0]} />
      <div className="my-3"><CloudLink resource="DEVICE_OWNERS" id={data.devices[0].id}>Registered owners</CloudLink></div>
      <p className="text-xs text-muted">A registered owner is registration evidence, not an Intune primary-user assignment.</p>
    </> : <CloudTable rows={data.devices} searchText={row => `${row.displayName ?? ''} ${row.deviceId ?? ''} ${row.operatingSystem ?? ''}`}
      columns={[
        { header: 'Device', cell: row => <CloudLink resource="DEVICE" id={row.id}>{available(row.displayName)}</CloudLink> },
        { header: 'Entra device ID', cell: row => available(row.deviceId), mono: true },
        { header: 'OS / version', cell: row => `${available(row.operatingSystem)} / ${available(row.operatingSystemVersion)}` },
        { header: 'Trust type', cell: row => available(row.trustType) },
        { header: 'Enabled', cell: row => available(row.accountEnabled) },
        { header: 'Approximate last sign-in', cell: row => timestamp(row.approximateLastSignInAtUtc) },
      ]} />;
  } else if (resource === 'MANAGED_DEVICES') {
    content = <CloudTable rows={data.managedDevices} searchText={row => `${row.deviceName ?? ''} ${row.userPrincipalName ?? ''} ${row.serialNumber ?? ''} ${row.complianceState ?? ''}`}
      columns={[
        { header: 'Device', cell: row => <details><summary className="cursor-pointer text-accent-400">{available(row.deviceName)}</summary><CloudManagedFields device={row} /></details> },
        { header: 'Associated user', cell: row => <CloudLink resource="USER" id={row.userId}>{available(row.userPrincipalName)}</CloudLink> },
        { header: 'OS / version', cell: row => `${available(row.operatingSystem)} / ${available(row.operatingSystemVersion)}` },
        { header: 'Compliance', cell: row => available(row.complianceState) },
        { header: 'Last Intune sync', cell: row => timestamp(row.lastSyncAtUtc) },
      ]} />;
  } else if (resource === 'LICENSES' || resource === 'USER_LICENSES') {
    content = <><p className="mb-3 text-xs text-muted">Graph supplies SKU part numbers and service-plan names, not a complete marketing product-name catalog. Enabled seats are usable purchased units; suspended/warning units are excluded. Remaining seats apply to user-based SKUs only.</p>
      <CloudTable rows={data.licenses} searchText={row => `${row.skuPartNumber ?? ''} ${row.skuId ?? ''}`}
        columns={[
          { header: 'SKU / product identifier', cell: row => <><div>{available(row.skuPartNumber)}</div><div className="font-mono text-xs text-muted">{available(row.skuId)}</div></> },
          ...(resource === 'LICENSES' ? [
            { header: 'State', cell: (row: typeof data.licenses[number]) => available(row.capabilityStatus) },
            { header: 'Enabled seats', cell: (row: typeof data.licenses[number]) => available(row.enabledSeats) },
            { header: 'Consumed', cell: (row: typeof data.licenses[number]) => available(row.consumedSeats) },
            { header: 'Remaining', cell: (row: typeof data.licenses[number]) => {
              const capacity = snapshot.licenseCapacity.find(item => item.skuId === row.skuId);
              return <>{available(capacity?.remainingSeats)} {capacity?.overAssigned ? <Badge tone="fail">Over-assigned</Badge>
                : capacity?.nearlyExhausted ? <Badge tone="warn">Nearly exhausted</Badge> : null}</>;
            } },
          ] : []),
          { header: 'Service plans', cell: row => <Plans plans={row.servicePlans} /> },
        ]} /></>;
  } else {
    content = <><p className="mb-3 text-xs text-muted">{data.members.length} loaded direct members. Limited-information objects remain visible by ID. Hidden members and service principals may be omitted by Graph v1.0; this is not a complete access audit.</p>
      <CloudTable rows={data.members} searchText={row => `${row.displayName ?? ''} ${row.id ?? ''}`}
        columns={[
          { header: 'Member', cell: row => <CloudLink resource={row.objectType === 'group' ? 'GROUP' : row.objectType === 'device' ? 'DEVICE' : 'USER'}
            id={['user', 'group', 'device'].includes(row.objectType ?? '') ? row.id : null}>{row.displayName ?? row.id ?? 'Limited information'}</CloudLink> },
          { header: 'Type', cell: row => available(row.objectType) },
          { header: 'Object ID', cell: row => available(row.id), mono: true },
        ]} /></>;
  }
  return <div className="flex flex-col gap-3">
    <p className="text-xs text-muted">Source: Microsoft Graph v1.0 · Retrieved {timestamp(snapshot.updatedAtUtc)} · {snapshot.stale ? 'Stale snapshot — refresh explicitly' : 'Cached snapshot'}
      {data.totalCount !== null && <> · Graph reported total: {data.totalCount} (eventual)</>}</p>
    {data.truncated && <div role="status" className="rounded border border-warn-600 p-3 text-sm text-warn-300">Partial inventory: the configured page/item limit was reached. Search and displayed rows cover the loaded subset only.</div>}
    {snapshot.refreshError && <p role="alert" className="text-sm text-fail-400">Refresh failed; the previous snapshot remains visible. {snapshot.refreshError.message}</p>}
    {content}
  </div>;
}
