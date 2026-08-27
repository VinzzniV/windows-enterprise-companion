import { describe, expect, it } from 'vitest';
import type { UserProfileResult } from '../../shared/api-types';
import { buildLeaverAssessment } from './leaverReview';
import { toLeaverReviewMarkdown } from './leaverReviewExport';

const profile = {
  identity: {
    objectId: '00112233-4455-6677-8899-aabbccddeeff',
    displayName: 'Alex\nExample',
    samAccountName: 'a.example',
    userPrincipalName: 'a.example@corp.example',
  },
  lifecycle: { enabled: true, replicatedLastLogonAtUtc: null },
  access: {
    directGroups: [{ name: 'GG-App', distinguishedName: 'CN=GG-App,OU=Groups,DC=corp,DC=example' }],
    privilegedCoverage: 'UNAVAILABLE',
    privilegedCoverageExplanation: 'Allowlist unavailable.',
    directPrivilegedGroups: [],
  },
  devices: {
    coverage: 'AVAILABLE',
    explanation: 'All stored devices evaluated.',
    totalLinkedDeviceCount: 1,
    linkedDevicesTruncated: false,
    linkedDevices: [{
      host: 'PC-42',
      inventoryCapturedAtUtc: '2026-08-27T08:00:00Z',
      relationshipEvidence: [{
        relationshipType: 'LAST_INTERACTIVE_USER',
        source: 'WEC Inventory',
        observedAtUtc: '2026-08-27T08:00:00Z',
        confidence: 'HIGH',
        explanation: 'Exact SID observation.',
        profileLastUseAtUtc: null,
      }],
    }],
  },
} as unknown as UserProfileResult;

describe('toLeaverReviewMarkdown', () => {
  it('exports deterministic session checks and explicit unresolved boundaries', () => {
    const markdown = toLeaverReviewMarkdown(
      profile,
      buildLeaverAssessment(profile),
      new Set(['account-state', 'direct-groups']),
      '2026-08-27T12:34:00.000Z',
    );

    expect(markdown).toContain('- User: Alex Example');
    expect(markdown).toContain('- [x] Directory account state — attention');
    expect(markdown).toContain('- [ ] Replicated last activity — unknown');
    expect(markdown).toContain('- GG-App — CN=GG-App,OU=Groups,DC=corp,DC=example');
    expect(markdown).toContain('- [ ] PC-42 — RETURN UNRESOLVED');
    expect(markdown).toContain('WEC persists no workflow state');
  });
});
