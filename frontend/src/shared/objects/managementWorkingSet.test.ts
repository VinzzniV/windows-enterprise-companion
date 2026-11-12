import { describe, expect, it } from 'vitest';
import type { ManagementDeviceObjectLists } from '../api-types.generated';
import { managementWorkingSetReads } from './workingSetSources';
import { captureWorkingSet } from './workingSet';
import { managementRecordPath, managementRecordReference } from './managementRecordRoutes';
import { workingSetSearch } from '../../app/workingSetSearch';

const snapshotId = '11111111-1111-1111-1111-111111111111';
const lists: ManagementDeviceObjectLists = { workspace: { scope: 'workspace', localComputerName: 'LOCAL' }, snapshotId, opsiSessionId: null,
  sessionRevision: 1, revision: 2, retrievedAtUtc: '2026-09-14T10:00:00Z', maximumRecords: 100, search: null,
  reads: ['KASPERSKY', 'OPSI', 'NESSUS'].map(source => ({ source: source as 'KASPERSKY' | 'OPSI' | 'NESSUS',
    state: { source, scope: source === 'NESSUS' ? null : 'source.example', availability: 'Available', error: null, loadedRecords: 2 },
    matchingCachedRecords: 2, limited: false, rows: [0, 1].map(recordIndex => ({
      reference: { workspace: 'workspace', snapshotId, source: source as 'KASPERSKY' | 'OPSI' | 'NESSUS', recordIndex },
      nativeReference: null, label: 'Same name', aliases: ['pc.example.test'], accountEnabled: null, operatingSystem: null, securityIdentifier: null,
    })),
  })),
};

describe('source-record list and route identity', () => {
  it('keeps duplicate names separate while reusing the same original snapshot observation across targeted queries', () => {
    const reads = managementWorkingSetReads(lists);
    const repeated = managementWorkingSetReads({ ...lists, search: 'Same', reads: [lists.reads[0]] });
    const snapshot = captureWorkingSet([...reads, ...repeated], { maximumRecords: 100, maximumSourceReads: 128, directoryScope: null, tenantId: null }, Date.now());
    expect(snapshot.loadedSourceRecords).toBe(8);
    expect(snapshot.objects).toHaveLength(6);
    expect(snapshot.confirmedObjectCount).toBe(0);
    expect(snapshot.objects.every(object => object.hasCandidates && object.references.length === 0)).toBe(true);
    expect(workingSetSearch('Same', snapshot).every(result => result.to.startsWith('/devices/records/'))).toBe(true);
    expect(reads[2].scope).toBeNull();
  });

  it('round-trips an exact source locator and rejects invalid or overflowing route values', () => {
    const record = lists.reads[0].rows[1].reference;
    expect(managementRecordPath(record)).toBe(`/devices/records/ksc/workspace/${snapshotId}/1`);
    expect(managementRecordReference('ksc', 'workspace', snapshotId, '1')).toEqual(record);
    for (const index of ['-1', '1.5', '01', '2147483648', '999999999999999999', 'bad']) expect(managementRecordReference('ksc', 'workspace', snapshotId, index)).toBeNull();
    expect(managementRecordReference('ksc', 'workspace', '00000000-0000-0000-0000-000000000000', '1')).toBeNull();
    expect(managementRecordReference('unknown', 'workspace', snapshotId, '1')).toBeNull();
  });
});
