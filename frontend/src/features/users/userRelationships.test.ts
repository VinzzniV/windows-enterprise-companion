import { describe, expect, it } from 'vitest';
import type { UserLinkedDeviceProfile, UserProfileResult } from '../../shared/api-types';
import { buildUserRelationshipModel } from './userRelationships';

const device: UserLinkedDeviceProfile = {
  host: 'PC-42.corp.example',
  inventoryCapturedAtUtc: '2026-08-27T08:00:00Z',
  relationshipEvidence: [
    {
      relationshipType: 'PROFILE_PRESENT',
      source: 'WEC Inventory',
      observedAtUtc: '2026-08-27T08:00:00Z',
      confidence: 'MEDIUM',
      explanation: 'The SID was present in a non-special local profile.',
      profileLastUseAtUtc: '2026-08-26T16:00:00Z',
    },
    {
      relationshipType: 'LAST_INTERACTIVE_USER',
      source: 'WEC Inventory',
      observedAtUtc: '2026-08-27T09:00:00Z',
      confidence: 'HIGH',
      explanation: 'The SID resolved from the last interactive domain user.',
      profileLastUseAtUtc: null,
    },
  ],
  software: {
    isAvailable: true,
    isComplete: true,
    capturedAtUtc: '2026-08-27T08:00:00Z',
    installedCount: 42,
    sample: [],
    explanation: 'Complete stored capture.',
  },
  health: {
    isAvailable: true,
    isComplete: true,
    capturedAtUtc: '2026-08-27T08:30:00Z',
    criticalCount: 1,
    warningCount: 2,
    unknownCount: 0,
    healthyCount: 1,
    explanation: 'All checks observed.',
  },
  security: {
    isAvailable: true,
    isComplete: true,
    capturedAtUtc: '2026-08-27T08:45:00Z',
    scanStatus: 'Completed',
    criticalCount: 0,
    highCount: 3,
    mediumCount: 2,
    lowCount: 1,
    explanation: 'All checks succeeded.',
  },
  vulnerabilities: {
    availability: 'AVAILABLE',
    deviceMatched: true,
    capturedAtUtc: '2026-08-27T07:00:00Z',
    criticalCount: 0,
    highCount: 2,
    mediumCount: 4,
    lowCount: 8,
    explanation: 'Stored Nessus match.',
  },
};

const profile = {
  identity: {
    objectId: '00112233-4455-6677-8899-aabbccddeeff',
    displayName: 'Alex Example',
    samAccountName: 'a.example',
    userPrincipalName: 'a.example@corp.example',
    department: 'IT',
  },
  lifecycle: { enabled: true },
  access: { directGroups: [{}, {}] },
  devices: { linkedDevices: [device] },
} as UserProfileResult;

describe('buildUserRelationshipModel', () => {
  it('builds a device link from explicit evidence without claiming ownership', () => {
    const model = buildUserRelationshipModel(profile);

    expect(model.nodes.map((node) => node.label)).toEqual(['Alex Example', 'PC-42.corp.example']);
    expect(model.nodes[1].context).toContain('42 installed apps');
    expect(model.nodes[1].href).toBe('/clients/PC-42.corp.example');
    expect(model.edges[0].relationshipType).toBe('Profile present + Last interactive user');
    expect(model.edges[0].confidence).toBe('high');
    expect(model.edges[0].observedAtUtc).toBe('2026-08-27T09:00:00.000Z');
    expect(model.edges[0].explanation).toContain('Profile last-use evidence');
    expect(model.description).toContain('not a device ownership');
  });

  it('keeps profile-only evidence visibly partial', () => {
    const model = buildUserRelationshipModel({
      ...profile,
      devices: {
        ...profile.devices,
        linkedDevices: [{ ...device, relationshipEvidence: [device.relationshipEvidence[0]] }],
      },
    });

    expect(model.nodes[1].status).toBe('partial');
    expect(model.edges[0].confidence).toBe('medium');
  });
});
