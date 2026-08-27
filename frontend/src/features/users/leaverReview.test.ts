import { describe, expect, it } from 'vitest';
import type { UserProfileResult } from '../../shared/api-types';
import { buildLeaverAssessment } from './leaverReview';

const profile = {
  identity: {
    objectId: '00112233-4455-6677-8899-aabbccddeeff',
    displayName: 'Alex Example',
    samAccountName: 'a.example',
    userPrincipalName: 'a.example@corp.example',
  },
  lifecycle: {
    enabled: true,
    replicatedLastLogonAtUtc: '2026-08-20T08:00:00Z',
  },
  access: {
    directGroups: [{ name: 'GG-App' }],
    privilegedCoverage: 'AVAILABLE',
    privilegedCoverageExplanation: 'Allowlist evaluated.',
    directPrivilegedGroups: [{ name: 'Domain Admins' }],
  },
  devices: {
    coverage: 'PARTIAL',
    explanation: 'Some snapshots predate user evidence.',
    totalLinkedDeviceCount: 1,
    linkedDevicesTruncated: false,
    linkedDevices: [{
      host: 'PC-42',
      inventoryCapturedAtUtc: '2026-08-20T09:00:00Z',
      relationshipEvidence: [{
        relationshipType: 'PROFILE_PRESENT',
        source: 'WEC Inventory',
        observedAtUtc: '2026-08-20T09:00:00Z',
        confidence: 'MEDIUM',
        explanation: 'Exact SID profile observation.',
        profileLastUseAtUtc: null,
      }],
    }],
  },
} as UserProfileResult;

describe('buildLeaverAssessment', () => {
  it('surfaces unresolved account, access and return evidence without claiming completion', () => {
    const assessment = buildLeaverAssessment(profile);

    expect(assessment.attentionCount).toBe(4);
    expect(assessment.unknownCount).toBe(1);
    expect(assessment.items.find((item) => item.id === 'account-state')?.summary).toContain('remains enabled');
    expect(assessment.items.find((item) => item.id === 'privileged-access')?.summary).toContain('membership remains');
    expect(assessment.devices[0]).toMatchObject({
      host: 'PC-42',
      relationship: 'Profile present',
      confidence: 'medium',
      returnState: 'unresolved',
    });
    expect(assessment.items.find((item) => item.id === 'device-return')?.evidence)
      .toContain('do not prove assignment, ownership or physical return');
  });

  it('keeps missing source coverage unknown even when the account is disabled', () => {
    const assessment = buildLeaverAssessment({
      ...profile,
      lifecycle: { ...profile.lifecycle, enabled: false, replicatedLastLogonAtUtc: null },
      access: {
        ...profile.access,
        directGroups: [],
        privilegedCoverage: 'UNAVAILABLE',
        directPrivilegedGroups: [],
      },
      devices: {
        ...profile.devices,
        coverage: 'NOT_CAPTURED',
        totalLinkedDeviceCount: 0,
        linkedDevices: [],
      },
    });

    expect(assessment.items.find((item) => item.id === 'account-state')?.state).toBe('verified');
    expect(assessment.items.find((item) => item.id === 'last-activity')?.state).toBe('unknown');
    expect(assessment.items.find((item) => item.id === 'privileged-access')?.state).toBe('unknown');
    expect(assessment.items.find((item) => item.id === 'device-return')?.state).toBe('unknown');
    expect(assessment.unknownCount).toBeGreaterThanOrEqual(4);
  });
});
