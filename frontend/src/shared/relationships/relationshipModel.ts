export type RelationshipEntityType =
  | 'device'
  | 'management-system'
  | 'inventory'
  | 'user'
  | 'group'
  | 'software'
  | 'health'
  | 'security';

export type RelationshipStatus = 'connected' | 'stale' | 'disconnected' | 'unknown' | 'partial';
export type RelationshipConfidence = 'confirmed' | 'high' | 'medium' | 'low' | 'unknown';

export interface RelationshipNode {
  id: string;
  entityType: RelationshipEntityType;
  label: string;
  context: string;
  status: RelationshipStatus;
  observedAtUtc: string | null;
  href?: string;
}

export interface RelationshipEdge {
  id: string;
  fromNodeId: string;
  toNodeId: string;
  relationshipType: string;
  evidenceSource: string;
  observedAtUtc: string | null;
  confidence: RelationshipConfidence;
  explanation: string;
}

export interface RelationshipMapModel {
  title: string;
  description: string;
  primaryNodeId: string;
  nodes: readonly RelationshipNode[];
  edges: readonly RelationshipEdge[];
}

export const MAX_RELATED_NODES = 8;
