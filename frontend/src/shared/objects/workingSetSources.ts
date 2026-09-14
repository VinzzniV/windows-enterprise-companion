import type { CachedDirectoryGroupPage, CachedDirectoryUserList, ManagementDeviceObjectLists, Microsoft365ObjectLists, ObjectReference, StoredObjectLists } from '../api-types.generated';
import { objectReference, objectSourceLabel } from './objectRoutes';
import type { WorkingSetObservation, WorkingSetRead, WorkingSetSource } from './workingSet';

export const workingSetSourceLabel: Record<WorkingSetSource, string> = { ...objectSourceLabel, KASPERSKY: 'Kaspersky', OPSI: 'opsi', NESSUS: 'Nessus' };

export function managementWorkingSetReads(lists: ManagementDeviceObjectLists): WorkingSetRead[] {
  return lists.reads.map(read => ({
    key: `management:${JSON.stringify([read.source, lists.search])}`, title: `${workingSetSourceLabel[read.source]} management records${lists.search ? ` · ${lists.search}` : ''}`,
    family: 'management', collectionKey: JSON.stringify(['management', lists.search]), scope: read.state.scope,
    sessionRevision: lists.sessionRevision, revision: JSON.stringify([lists.snapshotId, lists.revision, lists.opsiSessionId, read.state]),
    retrievedAtUtc: lists.retrievedAtUtc, lastAttemptAtUtc: null, retainedUntilUtc: null, freshUntilUtc: null,
    availability: ['Available', 'Partial', 'Truncated'].includes(read.state.availability) ? 'available'
      : read.state.availability === 'NotConnected' ? 'disconnected' : read.state.availability === 'NotLoaded' ? 'not-loaded' : 'unavailable',
    coverage: read.state.availability === 'Available' ? 'complete' : ['Partial', 'Truncated'].includes(read.state.availability) ? 'partial' : 'unknown',
    error: read.state.error, sourceTotal: read.matchingCachedRecords, cachedRecordCount: read.matchingCachedRecords ?? 0,
    limited: read.limited, rows: read.rows.map(row => ({ kind: 'DEVICE', source: read.source, reference: row.nativeReference,
      managementReference: row.reference, label: row.label, aliases: row.aliases, accountEnabled: row.accountEnabled,
      operatingSystem: row.operatingSystem, sid: row.securityIdentifier, registrationDeviceId: null, assignedSkuIds: null })),
  }));
}

export function cloudWorkingSetReads(lists: Microsoft365ObjectLists): WorkingSetRead[] {
  return lists.reads.map(read => ({
    key: `cloud:${JSON.stringify([lists.tenantId, read.state.query])}`,
    title: `Microsoft 365 · ${read.state.query.resource.toLowerCase().replaceAll('_', ' ')}${read.state.query.objectId ? ` · ${read.state.query.objectId}` : ''}${read.state.query.securityIdentifier ? ` · ${read.state.query.securityIdentifier}` : ''}`,
    family: 'cloud', scope: lists.tenantId,
    sessionRevision: lists.sessionRevision, revision: read.state.snapshotRevision,
    retrievedAtUtc: read.state.retrievedAtUtc, retainedUntilUtc: read.state.retainedUntilUtc, freshUntilUtc: read.state.freshUntilUtc,
    lastAttemptAtUtc: read.state.lastAttemptAtUtc,
    availability: read.state.availability === 'AVAILABLE' ? 'available' : read.state.availability === 'NOT_CACHED' ? 'not-loaded'
      : read.state.availability === 'NOT_CONNECTED' ? 'disconnected' : read.state.availability === 'NOT_ENABLED' ? 'disabled' : 'unavailable',
    coverage: read.state.coverage === 'RETURNED_SET' ? 'complete' : read.state.coverage === 'PARTIAL' ? 'partial' : 'unknown',
    error: read.state.lastAttemptError?.message ?? null, sourceTotal: read.state.declaredTotal,
    cachedRecordCount: read.state.loadedCount ?? 0, limited: lists.truncated || read.rows.length < (read.state.loadedCount ?? 0),
    resource: read.state.query.resource, querySid: read.state.query.securityIdentifier,
    rows: read.rows.map(row => ({
      reference: objectReference(row.kind, row.source === 'INTUNE' ? 'intune' : 'entra', lists.tenantId ?? undefined, row.objectId ?? undefined),
      kind: row.kind, source: row.source, label: row.displayName ?? row.objectId ?? 'Limited-information record',
      aliases: [row.userPrincipalName, row.objectId].filter((value): value is string => Boolean(value)),
      accountEnabled: row.accountEnabled, operatingSystem: row.operatingSystem, sid: row.securityIdentifier,
      registrationDeviceId: row.registrationDeviceId, assignedSkuIds: row.assignedSkuIds,
      nativeRecordId: row.objectId ?? undefined,
    })),
  }));
}

export interface DirectoryListSelection { scope: string; search: string; page: number; pageSize: number }

function directoryReadKey(kind: string, selection: DirectoryListSelection) {
  return `ad:${kind}:${JSON.stringify([selection.scope.toLowerCase(), selection.search.trim(), selection.page, selection.pageSize])}`;
}

export function directoryUserWorkingSetRead(read: CachedDirectoryUserList, selection: DirectoryListSelection): WorkingSetRead {
  const page = read.data;
  return {
    key: directoryReadKey('users', selection), title: 'Active Directory users', family: 'directory', scope: page?.directoryScope ?? selection.scope,
    collectionKey: JSON.stringify(['users', selection.scope, selection.search, selection.pageSize]),
    sessionRevision: read.sessionRevision, revision: read.revision, retrievedAtUtc: page?.retrievedAtUtc ?? null,
    lastAttemptAtUtc: read.lastAttemptAtUtc,
    retainedUntilUtc: read.retainedUntilUtc, freshUntilUtc: read.freshUntilUtc,
    availability: page ? 'available' : read.lastAttemptError ? 'unavailable' : 'not-loaded',
    coverage: page ? page.users.length === page.totalCount ? 'complete' : 'partial' : 'unknown',
    error: read.lastAttemptError?.message ?? null, sourceTotal: page?.totalCount ?? null,
    cachedRecordCount: page?.users.length ?? 0, limited: false,
    rows: page?.users.map(user => ({
      reference: objectReference('USER', 'ad', user.directoryScope, user.objectId), kind: 'USER', source: 'ACTIVE_DIRECTORY',
      label: user.displayName, aliases: [user.samAccountName, user.userPrincipalName, user.department, user.objectId].filter((value): value is string => Boolean(value)),
      accountEnabled: user.enabled, operatingSystem: null, sid: user.securityIdentifier, registrationDeviceId: null, assignedSkuIds: null,
      nativeRecordId: user.objectId,
    })) ?? [],
  };
}

export function directoryGroupWorkingSetRead(read: CachedDirectoryGroupPage, selection: DirectoryListSelection): WorkingSetRead {
  const page = read.data;
  return {
    key: directoryReadKey('groups', selection), title: 'Active Directory groups', family: 'directory', scope: page?.directoryScope ?? selection.scope,
    collectionKey: JSON.stringify(['groups', selection.scope, selection.search, selection.pageSize]),
    sessionRevision: read.state.sessionRevision, revision: read.state.revision, retrievedAtUtc: page?.retrievedAtUtc ?? null,
    lastAttemptAtUtc: read.state.lastAttemptAtUtc,
    retainedUntilUtc: read.state.retainedUntilUtc, freshUntilUtc: read.state.freshUntilUtc,
    availability: page ? 'available' : read.state.lastAttemptError ? 'unavailable' : 'not-loaded',
    coverage: page ? page.groups.length === page.totalCount ? 'complete' : 'partial' : 'unknown',
    error: read.state.lastAttemptError?.message ?? null, sourceTotal: page?.totalCount ?? null,
    cachedRecordCount: page?.groups.length ?? 0, limited: false,
    rows: page?.groups.map(group => ({
      reference: objectReference('GROUP', 'ad', group.directoryScope, group.objectId ?? undefined), kind: 'GROUP', source: 'ACTIVE_DIRECTORY',
      label: group.name, aliases: [group.samAccountName, group.distinguishedName].filter((value): value is string => Boolean(value)),
      accountEnabled: null, operatingSystem: null, sid: group.securityIdentifier, registrationDeviceId: null, assignedSkuIds: null,
      nativeRecordId: group.objectId ?? undefined,
    })) ?? [],
  };
}

export function storedAddressObservation(reference: ObjectReference, label = reference.id): WorkingSetObservation {
  return { reference, kind: 'DEVICE', source: 'WEC', label, aliases: [reference.id], accountEnabled: null,
    operatingSystem: null, sid: null, registrationDeviceId: null, assignedSkuIds: null };
}

export function storedWorkingSetReads(lists: StoredObjectLists): WorkingSetRead[] {
  return lists.reads.map(read => ({
    key: `stored:${JSON.stringify([read.source, lists.search])}`, title: read.source === 'INVENTORY' ? 'Stored Inventory'
      : read.source === 'SECURITY' ? 'Stored Security' : 'Saved client targets', family: 'stored', scope: lists.workspace.scope,
    collectionKey: JSON.stringify(['stored', lists.search]),
    sessionRevision: 0, revision: read.revision, retrievedAtUtc: lists.retrievedAtUtc, retainedUntilUtc: null, freshUntilUtc: null,
    lastAttemptAtUtc: lists.retrievedAtUtc,
    availability: read.error ? 'unavailable' : 'available', coverage: read.totalRecords === null ? 'unknown'
      : read.totalRecords > read.records.length ? 'partial' : 'complete', error: read.error?.message ?? null,
    sourceTotal: read.totalRecords, cachedRecordCount: read.records.length, limited: read.totalRecords !== null && read.records.length < read.totalRecords,
    rows: read.records.map(row => ({ ...storedAddressObservation({ kind: 'DEVICE', source: 'WEC', scope: lists.workspace.scope, id: row.host }, row.label),
      nativeRecordId: row.recordId, observedAtUtc: row.observedAtUtc })),
  }));
}
