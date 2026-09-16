import { describe, expect, it } from 'vitest';
import { cloudWorkingSetReads, directoryUserWorkingSetRead, storedWorkingSetReads } from './workingSetSources';
import type { Microsoft365ReadState } from '../api-types.generated';

const tenant = '11111111-1111-1111-1111-111111111111';
const id = '22222222-2222-2222-2222-222222222222';
const state: Microsoft365ReadState = {
  query: { resource: 'USERS', objectId: null, securityIdentifier: null }, tenantId: tenant, sessionRevision: 4, snapshotRevision: 8,
  availability: 'AVAILABLE', loading: false, retrievedAtUtc: '2026-09-14T10:00:00Z', lastAttemptAtUtc: '2026-09-14T10:05:00Z',
  lastAttemptError: { code: 'ACCESS_DENIED', message: 'Denied', details: null, requiredPrivilege: null }, retainedUntilUtc: '2026-09-14T11:00:00Z',
  freshness: 'STALE', coverage: 'PARTIAL', loadedCount: 2, declaredTotal: 200, freshUntilUtc: '2026-09-14T10:10:00Z',
};

describe('working-set source projection', () => {
  it('keeps cloud query coverage, an additional index bound and unknown assignments independent', () => {
    const reads = cloudWorkingSetReads({ tenantId: tenant, sessionRevision: 4, revision: 8, recordLimit: 1, cachedSourceRecords: 2,
      loadedSourceRecords: 1, truncated: true, reads: [{ state, rows: [{ kind: 'USER', source: 'ENTRA', objectId: null,
        displayName: 'Limited record', userPrincipalName: null, accountEnabled: null, operatingSystem: null,
        securityIdentifier: null, registrationDeviceId: null, associatedUserId: null, assignedSkuIds: null, complianceState: null, managementState: null,
        department: null, securityEnabled: null, mailEnabled: null, groupTypes: null }] }] });
    expect(reads[0].coverage).toBe('partial');
    expect(reads[0].error).toBe('Denied');
    expect(reads[0].sourceTotal).toBe(200);
    expect(reads[0].cachedRecordCount).toBe(2);
    expect(reads[0].limited).toBe(true);
    expect(reads[0].rows[0].reference).toBeNull();
    expect(reads[0].rows[0].assignedSkuIds).toBeNull();
    expect(reads[0].rows[0].accountEnabled).toBeNull();
  });

  it('retains exact directory scope, partial page counts and an unknown enabled state', () => {
    const source = directoryUserWorkingSetRead({ data: { directoryScope: 'example.test', retrievedAtUtc: state.retrievedAtUtc!,
      page: 2, pageSize: 100, totalCount: 250, users: [{ objectId: id, securityIdentifier: 'S-1-5-21-1-2-3-1001', directoryScope: 'example.test',
        displayName: 'Account', samAccountName: 'account', userPrincipalName: 'account@example.test', enabled: null, department: null }] },
      lastAttemptAtUtc: state.lastAttemptAtUtc, lastAttemptError: null, sessionRevision: 2, revision: 3,
      retainedUntilUtc: state.retainedUntilUtc, freshUntilUtc: state.freshUntilUtc, stale: false },
    { scope: 'example.test', search: 'Account', page: 2, pageSize: 100 });
    expect(source.sourceTotal).toBe(250);
    expect(source.coverage).toBe('partial');
    expect(source.rows[0].reference).toEqual({ kind: 'USER', source: 'ACTIVE_DIRECTORY', scope: 'example.test', id });
    expect(source.rows[0].accountEnabled).toBeNull();
    expect(source.rows[0].nativeRecordId).toBe(id);
  });

  it('does not infer device identity or freshness from a stored address and preserves source record IDs', () => {
    const [source] = storedWorkingSetReads({ workspace: { scope: 'workspace', localComputerName: 'LOCAL' }, maximumRecords: 1, maximumSourceReads: 128,
      retrievedAtUtc: state.retrievedAtUtc!, search: null, reads: [{ source: 'SECURITY', revision: 'content-hash', totalRecords: 100,
        error: null, records: [{ recordId: '9007199254740993', host: 'pc.b.example', label: 'pc.b.example', observedAtUtc: '2025-01-01T10:00:00Z' }] }] });
    expect(source.limited).toBe(true);
    expect(source.freshUntilUtc).toBeNull();
    expect(source.rows[0].registrationDeviceId).toBeNull();
    expect(source.rows[0].nativeRecordId).toBe('9007199254740993');
    expect(source.rows[0].observedAtUtc).toBe('2025-01-01T10:00:00Z');
    expect(source.rows[0].reference?.id).toBe('pc.b.example');
  });
});
