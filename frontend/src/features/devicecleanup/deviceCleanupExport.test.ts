import { describe, expect, it } from 'vitest';
import type { DeviceCleanupAssessment } from '../../shared/api-types';
import { toDeviceCleanupMarkdown } from './deviceCleanupExport';

const assessment: DeviceCleanupAssessment = {
  candidate: {
    subjectKey: 'PC-OLD',
    host: 'pc-old.corp.example',
    description: 'Accounting workstation',
    descriptionSource: 'Active Directory',
    classification: 'POTENTIAL_CLEANUP',
    classificationExplanation: 'AD exceeds the cleanup threshold.\nManual review required.',
    activeDirectoryExists: true,
    activeDirectoryEnabled: false,
    activeDirectoryLastLogonAtUtc: '2026-01-01T00:00:00Z',
    kasperskyExists: true,
    kasperskyLastSeenAtUtc: null,
    opsiExists: false,
    opsiLastSeenAtUtc: null,
    nessusExists: false,
    nessusLastScanAtUtc: null,
    inventoryExists: true,
    inventoryCapturedAtUtc: '2026-02-01T00:00:00Z',
    relevantFindingCount: 1,
  },
  sources: [{
    source: 'Active Directory',
    coverage: 'AVAILABLE',
    exists: true,
    state: 'Disabled',
    observedAtUtc: '2026-01-01T00:00:00Z',
    explanation: 'Directory evidence.',
  }],
  findings: [{ code: 'StaleAd', severity: 'Critical', message: 'Old directory activity.' }],
  userEvidenceAvailability: 'AVAILABLE',
  userEvidenceExplanation: 'One observation.',
  userObservations: [{
    relationshipType: 'LastInteractiveUser',
    sid: 'S-1-5-21-1-2-3-1104',
    accountDisplay: 'CORP\\alex',
    observedAtUtc: '2026-02-01T00:00:00Z',
    profileLastUseAtUtc: null,
    confidence: 'High',
    explanation: 'Observation only.',
  }],
};

describe('toDeviceCleanupMarkdown', () => {
  it('exports the session decision, source checklist and evidence boundaries deterministically', () => {
    const markdown = toDeviceCleanupMarkdown(
      assessment,
      new Set(['Active Directory']),
      'PREPARE_CLEANUP',
      'Replacement is confirmed.\nTicket reviewed.',
      { host: 'pc-old.corp.example', reachable: false, manageable: false },
      '2026-08-27T10:00:00Z',
    );

    expect(markdown).toContain('- Manual decision: Prepare controlled cleanup');
    expect(markdown).toContain('- Description: Accounting workstation');
    expect(markdown).toContain('- Required reason: Replacement is confirmed. Ticket reviewed.');
    expect(markdown).toContain('- [x] Active Directory — Disabled');
    expect(markdown).toContain('CORP\\alex — LastInteractiveUser');
    expect(markdown).toContain('No Ping or WinRM response; this is not proof that the device is retired');
    expect(markdown).toContain('does not disable, move or delete');
  });
});
