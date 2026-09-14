import { describe, expect, it } from 'vitest';
import { captureWorkingSet, type WorkingSetRead } from '../shared/objects/workingSet';
import { workingSetSearch } from './workingSetSearch';

const tenant = '11111111-1111-1111-1111-111111111111';
const makeRead = (count: number): WorkingSetRead => ({ key: 'entra', title: 'Entra', family: 'cloud', scope: tenant, sessionRevision: 1, revision: 1,
  retrievedAtUtc: null, lastAttemptAtUtc: null, retainedUntilUtc: null, freshUntilUtc: null, availability: 'available', coverage: 'partial',
  sourceTotal: 2000, cachedRecordCount: count * 3, limited: false,
  error: 'Previous refresh failed', rows: (['USER', 'DEVICE', 'GROUP'] as const).flatMap(kind => Array.from({ length: count }, (_, index) => ({
    reference: { kind, source: 'ENTRA' as const, scope: tenant, id: `22222222-2222-2222-2222-${String(index + 1).padStart(12, '0')}` },
    kind, source: 'ENTRA' as const, label: `Searchable ${kind} ${index}`, aliases: [], accountEnabled: null,
    operatingSystem: null, sid: null, registrationDeviceId: null, assignedSkuIds: null,
  }))),
});
const capture = (read: WorkingSetRead) => captureWorkingSet([read], { maximumRecords: 100, maximumSourceReads: 128, directoryScope: null, tenantId: tenant }, Date.now());

describe('working-set global search', () => {
  it('uses six deduplicated results per object kind including cloud-only devices and groups', () => {
    const results = workingSetSearch('searchable', capture(makeRead(10)));
    expect(results).toHaveLength(18);
    expect(results.filter(result => result.category === 'Groups')).toHaveLength(6);
    expect(results.find(result => result.category === 'Devices')?.to).toContain(`/devices/entra/${tenant}/`);
    expect(results.find(result => result.category === 'Users')?.to).toContain(`/users/entra/${tenant}/`);
  });

  it('opens the list for conflicting native records instead of choosing the first source observation', () => {
    const read = makeRead(1);
    read.rows = [...read.rows, { ...read.rows[0], label: 'Searchable conflicting account' }];
    const result = workingSetSearch('searchable', capture(read)).find(row => row.category === 'Users');
    expect(result?.to).toMatch(/^\/users\/workspace\?q=/);
    expect(result?.description).toContain('conflicting');
  });
});
