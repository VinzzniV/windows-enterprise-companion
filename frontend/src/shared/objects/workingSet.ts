import type { Microsoft365Resource, ObjectKind, ObjectReference, ObjectSource } from '../api-types.generated';
import { hostAddressKey } from '../targets/hostAddress';

export interface WorkingSetObservation {
  reference: ObjectReference | null;
  kind: ObjectKind;
  source: ObjectSource;
  label: string;
  aliases: readonly string[];
  accountEnabled: boolean | null;
  operatingSystem: string | null;
  sid: string | null;
  registrationDeviceId: string | null;
  assignedSkuIds: readonly string[] | null;
  nativeRecordId?: string;
  observedAtUtc?: string | null;
}

export interface WorkingSetRead {
  key: string;
  title: string;
  family: 'directory' | 'cloud' | 'stored' | 'management';
  collectionKey?: string;
  scope: string | null;
  sessionRevision: number;
  revision: number | string;
  retrievedAtUtc: string | null;
  lastAttemptAtUtc: string | null;
  retainedUntilUtc: string | null;
  freshUntilUtc: string | null;
  availability: 'available' | 'not-loaded' | 'unavailable' | 'disabled' | 'disconnected';
  coverage: 'complete' | 'partial' | 'unknown';
  error: string | null;
  sourceTotal: number | null;
  cachedRecordCount: number;
  limited: boolean;
  resource?: Microsoft365Resource;
  querySid?: string | null;
  rows: readonly WorkingSetObservation[];
}

export interface WorkingSetObject {
  key: string;
  kind: ObjectKind;
  label: string;
  references: readonly ObjectReference[];
  observations: readonly (WorkingSetObservation & { readKey: string })[];
  duplicateSourceIdentity: boolean;
  conflictingIdentityEvidence: boolean;
  hasCandidates: boolean;
}

export interface WorkingSetPolicy {
  maximumRecords: number;
  maximumSourceReads: number;
  directoryScope: string | null;
  tenantId: string | null;
}

export interface WorkingSetSnapshot {
  revision: string;
  reads: readonly WorkingSetRead[];
  objects: readonly WorkingSetObject[];
  loadedSourceRecords: number;
  confirmedObjectCount: number;
  unresolvedCandidateCount: number;
  limited: boolean;
  omittedSourceReads: number;
}

const guid = /^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const sidPattern = /^S-1-5-21-\d+-\d+-\d+-\d+$/i;
const canonical = (value: string) => value.trim().toLowerCase();
const order = (left: string, right: string) => left < right ? -1 : left > right ? 1 : 0;

export function workingSetReferenceKey(reference: ObjectReference): string {
  return JSON.stringify([reference.kind, reference.source, canonical(reference.scope),
    reference.source === 'WEC' ? hostAddressKey(reference.id) : canonical(reference.id)]);
}

function validReference(reference: ObjectReference | null): reference is ObjectReference {
  return Boolean(reference?.scope && reference.id && (reference.source === 'WEC' || guid.test(reference.id))
    && (reference.kind === 'DEVICE' || reference.source !== 'WEC' && reference.source !== 'INTUNE')
    && (!(reference.source === 'ENTRA' || reference.source === 'INTUNE') || guid.test(reference.scope)));
}

function uniqueValue(rows: WorkingSetObject['observations'], select: (row: WorkingSetObservation) => string | null) {
  const values = [...new Set(rows.map(select).filter((value): value is string => Boolean(value)).map(canonical))];
  return values.length === 1 ? values[0] : null;
}

function sameSourceObjects(reads: readonly WorkingSetRead[]): WorkingSetObject[] {
  const objects = new Map<string, MutableWorkingSetObject>();
  for (const read of reads) {
    read.rows.forEach((row, index) => {
      const reference = validReference(row.reference) && row.reference.kind === row.kind && row.reference.source === row.source
        && read.scope !== null && canonical(read.scope) === canonical(row.reference.scope) ? row.reference : null;
      const key = reference ? workingSetReferenceKey(reference) : JSON.stringify(['unresolved', read.key, index]);
      const previous = objects.get(key);
      const observation = { ...row, reference, readKey: read.key };
      if (previous) {
        previous.duplicateSourceIdentity ||= previous.observations.some(value => value.readKey === read.key);
        previous.observations.push(observation);
      } else {
        objects.set(key, { key, kind: row.kind, label: row.label, references: reference ? [reference] : [], observations: [observation],
          duplicateSourceIdentity: false, conflictingIdentityEvidence: false, hasCandidates: false });
      }
    });
  }
  return [...objects.values()];
}

type MutableWorkingSetObject = Omit<WorkingSetObject, 'references' | 'observations'> & {
  references: ObjectReference[];
  observations: Array<WorkingSetObservation & { readKey: string }>;
};

function sourceObjects(objects: readonly WorkingSetObject[], kind: ObjectKind, source: ObjectSource, scope: string | null) {
  return scope === null ? [] : objects.filter(object => object.kind === kind && object.references.length === 1
    && object.references[0].source === source && canonical(object.references[0].scope) === canonical(scope));
}

function completeRead(read: WorkingSetRead) {
  return read.availability === 'available' && read.coverage === 'complete' && !read.limited
    && read.cachedRecordCount === read.rows.length && (read.sourceTotal === null || read.sourceTotal <= read.cachedRecordCount);
}

function valueIndex(objects: readonly WorkingSetObject[], select: (row: WorkingSetObservation) => string | null) {
  const result = new Map<string, Set<WorkingSetObject>>();
  for (const object of objects) for (const value of new Set(object.observations.map(select).filter((value): value is string => Boolean(value)).map(canonical))) {
    if (!result.has(value)) result.set(value, new Set());
    result.get(value)!.add(object);
  }
  return result;
}

function provenValues(reads: readonly WorkingSetRead[], resource: Microsoft365Resource, select: (row: WorkingSetObservation) => string | null) {
  const proven = new Set<string>();
  for (const read of reads.filter(value => value.resource === resource && completeRead(value))) {
    const counts = new Map<string, number>();
    for (const row of read.rows) {
      const value = select(row);
      if (value) counts.set(canonical(value), (counts.get(canonical(value)) ?? 0) + 1);
    }
    for (const [value, count] of counts) if (count === 1) proven.add(value);
  }
  return proven;
}

function confirmedGroups(objects: WorkingSetObject[], reads: readonly WorkingSetRead[], policy: WorkingSetPolicy) {
  const root = new Map(objects.map(object => [object.key, object.key]));
  const find = (key: string): string => { let current = key; while (root.get(current) !== current) current = root.get(current)!; return current; };
  const join = (preferred: WorkingSetObject, related: WorkingSetObject) => root.set(find(related.key), find(preferred.key));
  const cloudUsers = sourceObjects(objects, 'USER', 'ENTRA', policy.tenantId);
  const directoryUsers = sourceObjects(objects, 'USER', 'ACTIVE_DIRECTORY', policy.directoryScope);
  const cloudReads = reads.filter(read => read.scope && policy.tenantId && canonical(read.scope) === canonical(policy.tenantId));
  const cloudLimited = cloudReads.some(read => read.limited);
  const directoryLimited = reads.some(read => read.family === 'directory' && read.scope && policy.directoryScope
    && canonical(read.scope) === canonical(policy.directoryScope) && read.limited);
  const directoryBySid = valueIndex(directoryUsers, row => row.sid);
  const cloudBySid = valueIndex(cloudUsers, row => row.sid);
  const provenSids = provenValues(cloudReads, 'USERS', row => row.sid);
  const unknownSids = new Set(cloudReads.flatMap(read => read.rows.filter(row => row.kind === 'USER' && !validReference(row.reference))
    .map(row => row.sid).filter((value): value is string => Boolean(value)).map(canonical)));
  for (const read of cloudReads) {
    if (read.resource === 'USERS_BY_SID' && read.querySid && completeRead(read) && read.rows.length === 1
      && read.rows[0].sid && canonical(read.rows[0].sid) === canonical(read.querySid)) provenSids.add(canonical(read.querySid));
  }
  for (const directory of directoryUsers) {
    const sid = uniqueValue(directory.observations, row => row.sid);
    if (cloudLimited || directoryLimited || !sid || !sidPattern.test(sid) || sid.split('-').slice(4).some(value => Number(value) > 0xffffffff)
      || directory.duplicateSourceIdentity) continue;
    const matches = [...cloudBySid.get(sid) ?? []];
    if (directoryBySid.get(sid)?.size !== 1 || matches.length !== 1 || unknownSids.has(sid) || matches[0].duplicateSourceIdentity
      || uniqueValue(matches[0].observations, row => row.sid) !== sid) continue;
    if (provenSids.has(sid)) join(directory, matches[0]);
  }
  const entraDevices = sourceObjects(objects, 'DEVICE', 'ENTRA', policy.tenantId);
  const intuneDevices = sourceObjects(objects, 'DEVICE', 'INTUNE', policy.tenantId);
  const entraByRegistration = valueIndex(entraDevices, row => row.registrationDeviceId);
  const intuneByRegistration = valueIndex(intuneDevices, row => row.registrationDeviceId);
  const provenRegistrations = provenValues(cloudReads, 'DEVICES', row => row.registrationDeviceId);
  const unknownRegistrations = new Set(cloudReads.flatMap(read => read.rows.filter(row => row.kind === 'DEVICE' && row.source === 'ENTRA' && !validReference(row.reference))
    .map(row => row.registrationDeviceId).filter((value): value is string => Boolean(value)).map(canonical)));
  for (const entra of entraDevices) {
    const registration = uniqueValue(entra.observations, row => row.registrationDeviceId);
    if (cloudLimited || !registration || !guid.test(registration) || entra.duplicateSourceIdentity
      || entraByRegistration.get(registration)?.size !== 1 || unknownRegistrations.has(registration) || !provenRegistrations.has(registration)) continue;
    for (const managed of intuneByRegistration.get(registration) ?? []) {
      if (!managed.duplicateSourceIdentity && uniqueValue(managed.observations, row => row.registrationDeviceId) === registration) join(entra, managed);
    }
  }
  const merged = new Map<string, MutableWorkingSetObject>();
  for (const object of objects) {
    const key = find(object.key);
    const previous = merged.get(key);
    if (previous) {
      previous.references.push(...object.references);
      previous.observations.push(...object.observations);
      previous.duplicateSourceIdentity ||= object.duplicateSourceIdentity;
    } else merged.set(key, { ...object, key, references: [...object.references], observations: [...object.observations] });
  }
  return [...merged.values()];
}

function names(object: WorkingSetObject) {
  return [...new Set(object.observations.flatMap(row => [row.label, ...row.aliases]).filter(Boolean).map(canonical))];
}

function candidateTokens(object: WorkingSetObject) {
  return [...names(object).map(name => `name:${object.kind}:${name}`), ...object.observations.flatMap(row => [
    ...(row.sid ? [`sid:${object.kind}:${canonical(row.sid)}`] : []),
    ...(row.registrationDeviceId && row.reference ? [`registration:${canonical(row.reference.scope)}:${canonical(row.registrationDeviceId)}`] : []),
  ])];
}

export function captureWorkingSet(input: readonly WorkingSetRead[], policy: WorkingSetPolicy, now: number): WorkingSetSnapshot {
  if (!Number.isSafeInteger(policy.maximumRecords) || policy.maximumRecords < 1) throw new Error('A positive working-set limit is required.');
  if (!Number.isSafeInteger(policy.maximumSourceReads) || policy.maximumSourceReads < 1) throw new Error('A positive source-read limit is required.');
  const omittedSourceReads = Math.max(0, input.length - policy.maximumSourceReads);
  const ordered = input.slice(-policy.maximumSourceReads).sort((a, b) => order(a.key, b.key));
  const visible = ordered.map(read => read.availability === 'available'
    && (read.retainedUntilUtc === null || Date.parse(read.retainedUntilUtc) > now) ? read.rows.length : 0);
  const selected = ordered.map(() => 0);
  let remaining = policy.maximumRecords;
  let active = ordered.map((_, index) => index).filter(index => visible[index] > 0);
  while (remaining > 0 && active.length > 0) {
    for (const index of active) { if (remaining <= 0) break; selected[index]++; remaining--; }
    active = active.filter(index => selected[index] < visible[index]);
  }
  let loaded = 0;
  const reads = ordered.map((read, index) => {
    const expired = read.retainedUntilUtc !== null && Date.parse(read.retainedUntilUtc) <= now;
    const available = read.availability === 'available' && !expired;
    const rows = available ? read.rows.slice(0, selected[index]) : [];
    loaded += rows.length;
    return structuredClone({ ...read, availability: expired ? 'not-loaded' as const : read.availability,
      limited: omittedSourceReads > 0 || read.limited || available && rows.length < read.rows.length, rows });
  });
  const objects = confirmedGroups(sameSourceObjects(reads), reads, policy);
  const byName = new Map<string, Set<string>>();
  for (const object of objects) for (const key of candidateTokens(object)) {
    if (!byName.has(key)) byName.set(key, new Set());
    byName.get(key)!.add(object.key);
  }
  for (const object of objects) {
    object.hasCandidates = candidateTokens(object).some(key => byName.get(key)!.size > 1);
    object.conflictingIdentityEvidence = [valueIndex([object], row => row.sid), valueIndex([object], row => row.registrationDeviceId)]
      .some(values => values.size > 1);
    const preferred = object.observations.filter(row => row.reference && workingSetReferenceKey(row.reference) === object.key);
    const labels = [...new Set((preferred.length ? preferred : object.observations).map(row => row.label).filter(Boolean))];
    object.label = labels.length === 1 ? labels[0] : object.references[0]?.id ?? labels.sort(order).join(' / ');
  }
  return {
    revision: JSON.stringify([policy, reads.map(read => [read.key, read.sessionRevision, read.revision, read.availability, read.rows.length])]),
    reads, objects: objects.sort((a, b) => order(canonical(a.label), canonical(b.label)) || order(a.key, b.key)),
    loadedSourceRecords: loaded,
    confirmedObjectCount: objects.filter(object => object.references.some(reference => reference.source !== 'WEC') && !object.duplicateSourceIdentity && !object.conflictingIdentityEvidence).length,
    unresolvedCandidateCount: objects.filter(object => object.hasCandidates || object.duplicateSourceIdentity || object.conflictingIdentityEvidence || object.references.length === 0).length,
    limited: reads.some(read => read.limited), omittedSourceReads,
  };
}

export interface WorkingSetFilter {
  kind?: ObjectKind;
  query?: string;
  source?: ObjectSource;
  accountState?: 'enabled' | 'disabled' | 'unknown';
  operatingSystem?: string;
  skuId?: string;
  descending?: boolean;
  page: number;
  pageSize: number;
}

export function queryWorkingSet(snapshot: WorkingSetSnapshot, filter: WorkingSetFilter) {
  const terms = canonical(filter.query ?? '').split(/\s+/).filter(Boolean);
  const objects = snapshot.objects.filter(object => {
    const rows = filter.source ? object.observations.filter(row => row.source === filter.source) : object.observations;
    return (!filter.kind || object.kind === filter.kind) && rows.length > 0
      && (!filter.accountState || rows.some(row => filter.accountState === 'unknown' ? row.accountEnabled === null : row.accountEnabled === (filter.accountState === 'enabled')))
      && (!filter.operatingSystem || rows.some(row => row.operatingSystem && canonical(row.operatingSystem) === canonical(filter.operatingSystem!)))
      && (!filter.skuId || rows.some(row => row.assignedSkuIds?.some(sku => canonical(sku) === canonical(filter.skuId!))))
      && terms.every(term => canonical([object.label, ...object.observations.flatMap(row => [row.label, ...row.aliases]),
        ...object.references.flatMap(reference => [reference.id, reference.scope])].join(' ')).includes(term));
  });
  if (filter.descending) objects.reverse();
  const pageSize = Math.max(1, Math.trunc(filter.pageSize) || 1);
  const page = Math.min(Math.max(1, Math.trunc(filter.page) || 1), Math.max(1, Math.ceil(objects.length / pageSize)));
  return { revision: snapshot.revision, total: objects.length, page, pageSize, rows: objects.slice((page - 1) * pageSize, page * pageSize) };
}
