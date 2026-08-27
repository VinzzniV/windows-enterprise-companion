import type {
  UserDeviceRelationshipObservation,
  UserLinkedDeviceProfile,
  UserProfileResult,
} from '../../shared/api-types';
import type {
  RelationshipConfidence,
  RelationshipEdge,
  RelationshipMapModel,
  RelationshipNode,
  RelationshipStatus,
} from '../../shared/relationships/relationshipModel';

const relationshipLabels = {
  LAST_INTERACTIVE_USER: 'Last interactive user',
  PROFILE_PRESENT: 'Profile present',
} as const;

function latestTimestamp(values: readonly (string | null)[]): string | null {
  const timestamps = values
    .map((value) => value ? Date.parse(value) : Number.NaN)
    .filter(Number.isFinite);
  return timestamps.length > 0 ? new Date(Math.max(...timestamps)).toISOString() : null;
}

function relationshipConfidence(observations: readonly UserDeviceRelationshipObservation[]): RelationshipConfidence {
  return observations.some((observation) => observation.confidence === 'HIGH') ? 'high' : 'medium';
}

function relationshipStatus(observations: readonly UserDeviceRelationshipObservation[]): RelationshipStatus {
  return observations.some((observation) => observation.confidence === 'HIGH') ? 'connected' : 'partial';
}

function relationshipType(observations: readonly UserDeviceRelationshipObservation[]): string {
  return [...new Set(observations.map((observation) => relationshipLabels[observation.relationshipType]))]
    .join(' + ');
}

function evidenceSource(observations: readonly UserDeviceRelationshipObservation[]): string {
  return [...new Set(observations.map((observation) => observation.source))].join(', ');
}

function evidenceExplanation(observations: readonly UserDeviceRelationshipObservation[]): string {
  return observations.map((observation) => {
    const profileUse = observation.profileLastUseAtUtc
      ? ` Profile last-use evidence: ${new Date(observation.profileLastUseAtUtc).toLocaleString()}.`
      : '';
    return `${relationshipLabels[observation.relationshipType]}: ${observation.explanation}${profileUse}`;
  }).join(' ');
}

function deviceContext(device: UserLinkedDeviceProfile): string {
  const software = device.software.isAvailable
    ? `${device.software.installedCount} installed apps`
    : 'Software unavailable';
  const health = device.health.isAvailable
    ? `Health ${device.health.criticalCount} critical/${device.health.warningCount} warning`
    : 'Health unavailable';
  const security = device.security.isAvailable
    ? `Security ${device.security.criticalCount} critical/${device.security.highCount} high`
    : 'Security unavailable';
  return `${health} · ${security} · ${software}`;
}

function relationshipObservedAt(device: UserLinkedDeviceProfile): string {
  return latestTimestamp(device.relationshipEvidence.map((observation) => observation.observedAtUtc))
    ?? device.inventoryCapturedAtUtc;
}

export function buildUserRelationshipModel(profile: UserProfileResult): RelationshipMapModel {
  const primaryId = `user:${profile.identity.objectId.toLocaleLowerCase()}`;
  const devices = profile.devices.linkedDevices;
  const relatedNodes: RelationshipNode[] = devices.map((device) => ({
    id: `device:${device.host.toLocaleLowerCase()}`,
    entityType: 'device',
    label: device.host,
    context: deviceContext(device),
    status: relationshipStatus(device.relationshipEvidence),
    observedAtUtc: relationshipObservedAt(device),
    href: `/clients/${encodeURIComponent(device.host)}`,
  }));
  const edges: RelationshipEdge[] = devices.map((device, index) => {
    const node = relatedNodes[index];
    return {
      id: `${primaryId}:${node.id}`,
      fromNodeId: primaryId,
      toNodeId: node.id,
      relationshipType: relationshipType(device.relationshipEvidence),
      evidenceSource: evidenceSource(device.relationshipEvidence),
      observedAtUtc: relationshipObservedAt(device),
      confidence: relationshipConfidence(device.relationshipEvidence),
      explanation: evidenceExplanation(device.relationshipEvidence),
    };
  });
  const accountState = profile.lifecycle.enabled === true
    ? 'Enabled account'
    : profile.lifecycle.enabled === false
      ? 'Disabled account'
      : 'Account state unknown';
  const directoryContext = [
    profile.identity.samAccountName ?? profile.identity.userPrincipalName,
    profile.identity.department,
    `${profile.access.directGroups.length} direct groups`,
    accountState,
  ].filter(Boolean).join(' · ');
  const primary: RelationshipNode = {
    id: primaryId,
    entityType: 'user',
    label: profile.identity.displayName,
    context: directoryContext,
    status: 'connected',
    observedAtUtc: latestTimestamp(relatedNodes.map((node) => node.observedAtUtc)),
    href: `/users/${encodeURIComponent(profile.identity.objectId)}`,
  };

  return {
    title: 'User relationships',
    description: 'SID-matched Inventory observations. A relationship is evidence, not a device ownership or assignment claim.',
    primaryNodeId: primaryId,
    nodes: [primary, ...relatedNodes],
    edges,
  };
}
