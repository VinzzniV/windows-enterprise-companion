import { describe, expect, it } from 'vitest';
import type { HygieneStatus } from '../../shared/api-types';
import {
  clientConnectivityStatus,
  hygieneAssessmentStatus,
  snapshotAvailabilityStatus,
  sourceFreshnessStatus,
  sourcePresenceStatus,
} from './clientStatus';

const assessmentCases: Array<[HygieneStatus | null, string, string, string | null]> = [
  ['HEALTHY', 'health', 'healthy', null],
  ['WARNING', 'health', 'warning', null],
  ['CLEANUP_CANDIDATE', 'health', 'critical', 'Cleanup candidate'],
  ['CRITICAL', 'health', 'critical', null],
  ['INCOMPLETE', 'execution', 'partial', 'Assessment incomplete'],
  [null, 'availability', 'unknown', 'Unmanaged'],
];

describe('client semantic status', () => {
  it.each([
    [{ kind: 'loading' }, 'execution', 'running', 'Connectivity check'],
    [{ kind: 'failed' }, 'execution', 'failed', 'Connectivity check'],
    [{ kind: 'unavailable' }, 'availability', 'unknown', 'No probe result'],
    [{ kind: 'loaded', probe: { host: 'PC01', reachable: true, manageable: true } }, 'availability', 'available', 'Ping + WinRM 5985'],
    [{ kind: 'loaded', probe: { host: 'PC01', reachable: true, manageable: false } }, 'availability', 'available', 'Ping response · No WinRM response'],
    [{ kind: 'loaded', probe: { host: 'PC01', reachable: false, manageable: true } }, 'availability', 'available', 'WinRM 5985 open · No ping response'],
    [{ kind: 'loaded', probe: { host: 'PC01', reachable: false, manageable: false } }, 'availability', 'unknown', 'No ping or WinRM response'],
  ] as const)('maps connectivity state %# without claiming a device is offline', (state, dimension, value, context) => {
    expect(clientConnectivityStatus(state)).toEqual({ status: { dimension, value }, context });
  });

  it.each(assessmentCases)('maps assessment %s without losing its operational context', (raw, dimension, value, context) => {
    expect(hygieneAssessmentStatus(raw)).toEqual({ status: { dimension, value }, context });
  });

  it.each([
    [true, false, 'available'],
    [false, true, 'missing'],
    [false, false, 'not-applicable'],
  ] as const)('maps source presence %s/%s to %s', (present, missingApplies, value) => {
    expect(sourcePresenceStatus(present, missingApplies)).toEqual({ dimension: 'availability', value });
  });

  it.each([
    [true, '2026-08-19T10:00:00Z', 'stale'],
    [false, null, 'unknown'],
    [false, '2026-08-19T10:00:00Z', 'fresh'],
  ] as const)('maps source freshness %s/%s to %s', (stale, timestamp, value) => {
    expect(sourceFreshnessStatus(stale, timestamp)).toEqual(
      value === 'unknown'
        ? { dimension: 'availability', value: 'unknown' }
        : { dimension: 'freshness', value },
    );
  });

  it.each([
    [true, 'available'],
    [false, 'missing'],
  ] as const)('maps snapshot availability %s to %s', (available, value) => {
    expect(snapshotAvailabilityStatus(available)).toEqual({ dimension: 'availability', value });
  });
});
