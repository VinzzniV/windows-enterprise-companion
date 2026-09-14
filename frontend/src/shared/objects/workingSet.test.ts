import { describe, expect, it } from 'vitest';
import { captureWorkingSet, queryWorkingSet, type WorkingSetObservation, type WorkingSetPolicy, type WorkingSetRead } from './workingSet';

const tenant = '11111111-1111-1111-1111-111111111111';
const adId = '22222222-2222-2222-2222-222222222222';
const cloudId = '33333333-3333-3333-3333-333333333333';
const extraId = '44444444-4444-4444-4444-444444444444';
const registration = '55555555-5555-5555-5555-555555555555';
const sid = 'S-1-5-21-1-2-3-1001';
const now = Date.parse('2026-09-14T10:00:00Z');
const policy: WorkingSetPolicy = { maximumRecords: 100, maximumSourceReads: 128, directoryScope: 'example.test', tenantId: tenant };
const ad = (id = adId, label = 'Account'): WorkingSetObservation => ({
  kind: 'USER', source: 'ACTIVE_DIRECTORY', reference: { kind: 'USER', source: 'ACTIVE_DIRECTORY', scope: 'example.test', id },
  label, aliases: [label, 'account@example.test'], accountEnabled: null, operatingSystem: null, sid, registrationDeviceId: null, assignedSkuIds: null,
});
const cloud = (id = cloudId, label = 'Cloud account'): WorkingSetObservation => ({ ...ad(id, label), source: 'ENTRA',
  reference: { kind: 'USER', source: 'ENTRA', scope: tenant, id }, assignedSkuIds: ['sku'], accountEnabled: true });
const read = (key: string, rows: WorkingSetObservation[], extra: Partial<WorkingSetRead> = {}): WorkingSetRead => ({
  key, title: key, family: key === 'ad' ? 'directory' : 'cloud', scope: rows[0]?.reference?.scope ?? tenant, sessionRevision: 1, revision: 1,
  retrievedAtUtc: '2026-09-14T09:55:00Z', lastAttemptAtUtc: '2026-09-14T09:55:00Z', retainedUntilUtc: '2026-09-14T10:55:00Z', freshUntilUtc: '2026-09-14T10:05:00Z',
  availability: 'available', coverage: 'complete', error: null, sourceTotal: rows.length, cachedRecordCount: rows.length, limited: false, rows, ...extra,
});
const capture = (reads: WorkingSetRead[], overrides: Partial<WorkingSetPolicy> = {}) => captureWorkingSet(reads, { ...policy, ...overrides }, now);
const devices = (source: 'ENTRA' | 'INTUNE', id: string): WorkingSetObservation => ({ ...cloud(id, 'Device'), kind: 'DEVICE', source,
  reference: { kind: 'DEVICE', source, scope: tenant, id }, sid: null, registrationDeviceId: registration, assignedSkuIds: null });

describe('bounded object working set', () => {
  it('bounds source metadata as well as records and disables cross-source proof after a source read was omitted', () => {
    const snapshot = capture([read('old', [cloud(extraId)]), read('ad', [ad()]), read('cloud', [cloud()], { resource: 'USERS' })], { maximumSourceReads: 2 });
    expect(snapshot.reads).toHaveLength(2);
    expect(snapshot.omittedSourceReads).toBe(1);
    expect(snapshot.limited).toBe(true);
    expect(snapshot.objects).toHaveLength(2);
  });
  it('deduplicates scoped source IDs and confirmed account links before filtering, sorting and paging', () => {
    const snapshot = capture([read('ad', [ad()]), read('cloud', [cloud()], { resource: 'USERS' }),
      read('detail', [cloud()], { resource: 'USER' })]);
    expect(snapshot.loadedSourceRecords).toBe(3);
    expect(snapshot.objects).toHaveLength(1);
    expect(snapshot.objects[0].references).toHaveLength(2);
    expect(snapshot.objects[0].observations).toHaveLength(3);
    expect(snapshot.objects[0].label).toBe('Account');
    expect(snapshot.confirmedObjectCount).toBe(1);
    const result = queryWorkingSet(snapshot, { source: 'ENTRA', query: 'account', skuId: 'sku', page: 5, pageSize: 1 });
    expect(result.total).toBe(1);
    expect(result.page).toBe(1);
    expect(result.rows).toHaveLength(1);
    expect(queryWorkingSet(snapshot, { source: 'ACTIVE_DIRECTORY', accountState: 'enabled', page: 1, pageSize: 6 }).total).toBe(0);
    expect(queryWorkingSet(snapshot, { source: 'ACTIVE_DIRECTORY', accountState: 'unknown', page: 1, pageSize: 6 }).total).toBe(1);
  });

  it('never turns a partial collection or a UPN into proof and requires the selected authority pair', () => {
    const sources = [read('ad', [ad()]), read('cloud', [cloud()], { resource: 'USERS', coverage: 'partial', sourceTotal: 500 })];
    expect(capture(sources).objects).toHaveLength(2);
    expect(capture(sources).unresolvedCandidateCount).toBe(2);
    expect(capture([sources[0], read('cloud', [cloud()], { resource: 'USERS' })], { tenantId: null }).objects).toHaveLength(2);
    expect(capture([sources[0], read('cloud', [{ ...cloud(), sid: null }], { resource: 'USERS' })]).objects).toHaveLength(2);
    expect(capture([sources[0], read('cloud', [cloud()], { resource: 'USERS' })], { directoryScope: 'other.test' }).objects).toHaveLength(2);
  });

  it('allows a bounded exact SID proof while retaining incomplete inventory coverage', () => {
    const snapshot = capture([read('ad', [ad()]), read('cloud', [cloud()], { resource: 'USERS', coverage: 'partial', sourceTotal: 500 }),
      read('sid', [cloud()], { resource: 'USERS_BY_SID', querySid: sid })]);
    expect(snapshot.objects).toHaveLength(1);
    expect(snapshot.reads.find(source => source.key === 'cloud')?.sourceTotal).toBe(500);
  });

  it('blocks joins for colliding SIDs, duplicate native records and conflicting cached identity fields', () => {
    const source = read('cloud', [cloud()], { resource: 'USERS' });
    expect(capture([read('ad', [ad()]), { ...source, rows: [cloud(), cloud(extraId)], cachedRecordCount: 2 }]).objects).toHaveLength(3);
    const duplicate = capture([read('ad', [ad()]), { ...source, rows: [cloud(), cloud()], cachedRecordCount: 2 }]);
    expect(duplicate.objects).toHaveLength(2);
    expect(duplicate.objects.some(object => object.duplicateSourceIdentity)).toBe(true);
    const conflict = capture([read('ad', [ad()]), source, read('detail', [{ ...cloud(), sid: 'S-1-5-21-9-8-7-1001' }], { resource: 'USER' })]);
    expect(conflict.objects).toHaveLength(2);
    expect(conflict.objects.some(object => object.conflictingIdentityEvidence)).toBe(true);
  });

  it('does not hide a potential identity collision behind the working-set limit', () => {
    const snapshot = capture([read('ad', [ad()]), read('cloud', [cloud()], { resource: 'USERS' }),
      read('extra', [cloud(extraId)], { resource: 'USER' })], { maximumRecords: 2 });
    expect(snapshot.loadedSourceRecords).toBe(2);
    expect(snapshot.limited).toBe(true);
    expect(snapshot.objects).toHaveLength(2);
  });

  it('shares the record bound between source reads so a large inventory cannot hide every other source', () => {
    const snapshot = capture([read('ad', [ad(), ad(extraId), ad(cloudId)]), read('cloud', [cloud()], { resource: 'USERS' })], { maximumRecords: 2 });
    expect(snapshot.reads.map(source => source.rows.length)).toEqual([1, 1]);
    expect(snapshot.limited).toBe(true);
    expect(snapshot.objects).toHaveLength(2);
  });

  it('retains all Intune enrollment references for a uniquely proven Entra registration', () => {
    const inputs = [read('entra', [devices('ENTRA', cloudId)], { resource: 'DEVICES' }),
      read('intune', [devices('INTUNE', adId), devices('INTUNE', extraId)], { resource: 'MANAGED_DEVICES' })];
    const snapshot = capture(inputs);
    expect(snapshot.objects).toHaveLength(1);
    expect(snapshot.objects[0].references).toHaveLength(3);
    expect(capture([{ ...inputs[0], coverage: 'partial' }, inputs[1]]).objects).toHaveLength(3);
    const collision = read('detail', [devices('ENTRA', extraId)], { resource: 'DEVICE' });
    expect(capture([...inputs, collision]).objects).toHaveLength(4);
  });

  it('keeps unknown native IDs visible and prevents them from supporting a join', () => {
    const snapshot = capture([read('ad', [ad()]), read('cloud', [cloud(), { ...cloud(), reference: null }], { resource: 'USERS' })]);
    expect(snapshot.objects).toHaveLength(3);
    expect(snapshot.objects.some(object => object.references.length === 0)).toBe(true);
  });

  it('keeps full Windows addresses and IPs separate from name candidates', () => {
    const local = (id: string): WorkingSetObservation => ({ ...devices('ENTRA', cloudId), source: 'WEC',
      reference: { kind: 'DEVICE', source: 'WEC', scope: 'workspace', id }, label: id, aliases: [], registrationDeviceId: null });
    const snapshot = capture([read('local', [local('pc.a.example'), local('pc.b.example'), local('192.0.2.10'), local('192.0.2.11')])]);
    expect(snapshot.objects).toHaveLength(4);
    expect(snapshot.confirmedObjectCount).toBe(0);
  });

  it('freezes the captured rows against later input changes and keeps query revisions stable', () => {
    const mutable = ad();
    const inputs = [read('ad', [mutable])];
    const snapshot = capture(inputs);
    mutable.label = 'Changed';
    expect(snapshot.objects[0].label).toBe('Account');
    expect(queryWorkingSet(snapshot, { query: 'changed', page: 1, pageSize: 6 }).total).toBe(0);
    expect(queryWorkingSet(snapshot, { query: 'account', page: 1, pageSize: 6 }).revision).toBe(snapshot.revision);
    expect(capture([{ ...inputs[0], revision: 2 }]).revision).not.toBe(snapshot.revision);
  });

  it('separates expired, failed and never-read sources from an empty successful result', () => {
    const snapshot = capture([read('expired', [cloud()], { retainedUntilUtc: '2026-09-14T09:00:00Z' }),
      read('failed', [], { availability: 'unavailable', error: 'Denied', coverage: 'unknown', sourceTotal: null }), read('empty', [])]);
    expect(snapshot.objects).toHaveLength(0);
    expect(snapshot.reads.find(source => source.key === 'expired')?.availability).toBe('not-loaded');
    expect(snapshot.reads.find(source => source.key === 'failed')?.error).toBe('Denied');
    expect(snapshot.reads.find(source => source.key === 'empty')?.sourceTotal).toBe(0);
  });
});
