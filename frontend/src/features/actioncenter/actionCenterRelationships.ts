import type { ActionCenterWorkItem } from '../../shared/api-types';
import type {
  RelationshipConfidence,
  RelationshipEntityType,
  RelationshipMapModel,
  RelationshipStatus,
} from '../../shared/relationships/relationshipModel';

function sourceEntityType(source: string): RelationshipEntityType {
  if (source === 'WEC Inventory') return 'inventory';
  if (source === 'WEC Security') return 'security';
  return 'management-system';
}

function sourceStatus(item: ActionCenterWorkItem): RelationshipStatus {
  if (item.coverage === 'AVAILABLE') return item.problemCode.includes('Stale') || item.problemCode === 'INVENTORY_STALE'
    ? 'stale'
    : 'connected';
  if (item.coverage === 'PARTIAL' || item.coverage === 'TRUNCATED') return 'partial';
  if (item.coverage === 'NOT_CONNECTED') return 'disconnected';
  return 'unknown';
}

function confidence(item: ActionCenterWorkItem): RelationshipConfidence {
  if (item.reliability === 'High') return 'high';
  if (item.reliability === 'Medium') return 'medium';
  return 'unknown';
}

export function actionCenterRelationshipModel(item: ActionCenterWorkItem): RelationshipMapModel {
  const deviceId = `device:${item.subjectKey}`;
  const sourceId = `source:${item.source.toLocaleLowerCase().replaceAll(/[^a-z0-9]+/g, '-')}`;
  return {
    title: `Action context · ${item.device}`,
    description: 'Only the affected device, reporting source and evidence used by this work item.',
    primaryNodeId: deviceId,
    nodes: [
      {
        id: deviceId,
        entityType: 'device',
        label: item.device,
        context: `${item.severity.toLocaleLowerCase()}: ${item.problem}`,
        status: 'unknown',
        observedAtUtc: item.assessedAtUtc,
        href: `/clients/${encodeURIComponent(item.device)}`,
      },
      {
        id: sourceId,
        entityType: sourceEntityType(item.source),
        label: item.source,
        context: `${item.coverage.toLocaleLowerCase()} coverage · ${item.reliability.toLocaleLowerCase()} reliability`,
        status: sourceStatus(item),
        observedAtUtc: item.evidenceAtUtc,
        href: item.href,
      },
    ],
    edges: [{
      id: `${deviceId}:${sourceId}:${item.problemCode}`,
      fromNodeId: deviceId,
      toNodeId: sourceId,
      relationshipType: 'Reported by',
      evidenceSource: item.source,
      observedAtUtc: item.evidenceAtUtc,
      confidence: confidence(item),
      explanation: item.coverageExplanation,
    }],
  };
}
