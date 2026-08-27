import { describe, expect, it } from 'vitest';
import type { ActionCenterWorkItem } from '../../shared/api-types';
import { actionCenterRelationshipModel } from './actionCenterRelationships';

const item: ActionCenterWorkItem = {
  id: 'inventory:PC-42:stale',
  subjectType: 'Device',
  subjectKey: 'PC-42',
  device: 'PC-42',
  userObjectId: null,
  userDisplayName: null,
  problemCode: 'INVENTORY_STALE',
  problem: 'Stored Inventory snapshot is stale',
  explanation: 'The snapshot is old.',
  source: 'WEC Inventory',
  severity: 'WARNING',
  evidenceAtUtc: '2026-07-01T08:00:00Z',
  assessedAtUtc: '2026-08-27T08:00:00Z',
  evidenceAgeDays: 57,
  coverage: 'AVAILABLE',
  reliability: 'High',
  coverageExplanation: 'The latest stored timestamp was read without starting a scan.',
  recommendedAction: 'Open Inventory.',
  href: '/clients/PC-42?section=inventory',
};

describe('actionCenterRelationshipModel', () => {
  it('builds only the selected device, reporting source and evidence edge', () => {
    const model = actionCenterRelationshipModel(item);

    expect(model.nodes).toHaveLength(2);
    expect(model.nodes[0]).toMatchObject({
      entityType: 'device',
      label: 'PC-42',
      href: '/clients/PC-42',
    });
    expect(model.nodes[1]).toMatchObject({
      entityType: 'inventory',
      label: 'WEC Inventory',
      status: 'stale',
      href: '/clients/PC-42?section=inventory',
    });
    expect(model.edges[0]).toMatchObject({
      relationshipType: 'Reported by',
      evidenceSource: 'WEC Inventory',
      confidence: 'high',
    });
    expect(model.nodes.some((node) => node.entityType === 'user')).toBe(false);
  });
});
