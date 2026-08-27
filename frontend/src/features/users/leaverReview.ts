import type { UserLinkedDeviceProfile, UserProfileResult } from '../../shared/api-types';

export type LeaverEvidenceState = 'attention' | 'verified' | 'unknown' | 'information';

export interface LeaverReviewItem {
  id: string;
  title: string;
  state: LeaverEvidenceState;
  summary: string;
  evidence: string;
  source: string;
  href?: string;
}

export interface LeaverDeviceReturnEvidence {
  host: string;
  relationship: string;
  confidence: 'high' | 'medium';
  observedAtUtc: string;
  returnState: 'unresolved';
  explanation: string;
  href: string;
}

export interface LeaverAssessment {
  userObjectId: string;
  userDisplayName: string;
  accountName: string | null;
  items: readonly LeaverReviewItem[];
  devices: readonly LeaverDeviceReturnEvidence[];
  attentionCount: number;
  unknownCount: number;
}

const relationshipLabels = {
  LAST_INTERACTIVE_USER: 'Last interactive user',
  PROFILE_PRESENT: 'Profile present',
} as const;

function accountItem(profile: UserProfileResult): LeaverReviewItem {
  if (profile.lifecycle.enabled === false) {
    return {
      id: 'account-state',
      title: 'Directory account state',
      state: 'verified',
      summary: 'The AD account is disabled.',
      evidence: 'Disabled is the current directory state. This review did not change it.',
      source: 'Active Directory',
    };
  }
  if (profile.lifecycle.enabled === true) {
    return {
      id: 'account-state',
      title: 'Directory account state',
      state: 'attention',
      summary: 'The AD account remains enabled.',
      evidence: 'Account disablement is outside this read-only workflow and must be handled through approved administration procedures.',
      source: 'Active Directory',
    };
  }
  return {
    id: 'account-state',
    title: 'Directory account state',
    state: 'unknown',
    summary: 'The AD account state is unavailable.',
    evidence: 'Do not assume that the account is disabled.',
    source: 'Active Directory',
  };
}

function activityItem(profile: UserProfileResult): LeaverReviewItem {
  return profile.lifecycle.replicatedLastLogonAtUtc
    ? {
        id: 'last-activity',
        title: 'Replicated last activity',
        state: 'information',
        summary: 'AD returned a replicated lastLogonTimestamp.',
        evidence: `${profile.lifecycle.replicatedLastLogonAtUtc}. This value can be stale and is not exact sign-in evidence.`,
        source: 'Active Directory lastLogonTimestamp',
      }
    : {
        id: 'last-activity',
        title: 'Replicated last activity',
        state: 'unknown',
        summary: 'No replicated lastLogonTimestamp is available.',
        evidence: 'Missing activity evidence is not proof that the account was unused.',
        source: 'Active Directory lastLogonTimestamp',
      };
}

function directGroupsItem(profile: UserProfileResult): LeaverReviewItem {
  const count = profile.access.directGroups.length;
  return count > 0
    ? {
        id: 'direct-groups',
        title: 'Remaining direct groups',
        state: 'attention',
        summary: `${count} direct group ${count === 1 ? 'membership remains' : 'memberships remain'}.`,
        evidence: 'Review every direct membership before concluding that access has been withdrawn.',
        source: 'Active Directory memberOf',
        href: '?section=access',
      }
    : {
        id: 'direct-groups',
        title: 'Remaining direct groups',
        state: 'verified',
        summary: 'No direct group memberships were returned.',
        evidence: 'Nested or external authorization is not represented by this direct-membership result.',
        source: 'Active Directory memberOf',
        href: '?section=access',
      };
}

function privilegedItem(profile: UserProfileResult): LeaverReviewItem {
  if (profile.access.privilegedCoverage !== 'AVAILABLE') {
    return {
      id: 'privileged-access',
      title: 'Privileged access context',
      state: 'unknown',
      summary: 'Privileged-group classification coverage is unavailable.',
      evidence: profile.access.privilegedCoverageExplanation,
      source: 'SID-validated privileged-group allowlist',
      href: '?section=access',
    };
  }
  const count = profile.access.directPrivilegedGroups.length;
  return count > 0
    ? {
        id: 'privileged-access',
        title: 'Privileged access context',
        state: 'attention',
        summary: `${count} direct privileged ${count === 1 ? 'membership remains' : 'memberships remain'}.`,
        evidence: profile.access.privilegedCoverageExplanation,
        source: 'SID-validated privileged-group allowlist',
        href: '?section=access',
      }
    : {
        id: 'privileged-access',
        title: 'Privileged access context',
        state: 'verified',
        summary: 'No direct privileged membership was found by the configured allowlist.',
        evidence: profile.access.privilegedCoverageExplanation,
        source: 'SID-validated privileged-group allowlist',
        href: '?section=access',
      };
}

function deviceReturnItem(profile: UserProfileResult): LeaverReviewItem {
  const count = profile.devices.totalLinkedDeviceCount;
  if (count > 0) {
    return {
      id: 'device-return',
      title: 'Linked-device return',
      state: 'attention',
      summary: `${count} linked ${count === 1 ? 'device requires' : 'devices require'} a manual return decision.`,
      evidence: 'Interactive-user and profile observations do not prove assignment, ownership or physical return.',
      source: 'WEC Inventory user evidence',
      href: '?section=devices',
    };
  }
  return {
    id: 'device-return',
    title: 'Linked-device return',
    state: 'unknown',
    summary: 'No exact SID-matched linked device is available.',
    evidence: 'The absence of Inventory observations does not prove that no device is assigned or outstanding.',
    source: 'WEC Inventory user evidence',
    href: '?section=devices',
  };
}

function deviceCoverageItem(profile: UserProfileResult): LeaverReviewItem {
  const { devices } = profile;
  if (devices.coverage === 'AVAILABLE' && !devices.linkedDevicesTruncated) {
    return {
      id: 'device-coverage',
      title: 'Device evidence coverage',
      state: 'information',
      summary: 'All stored Inventory devices were evaluated for approved user evidence.',
      evidence: devices.explanation,
      source: 'WEC Inventory latest snapshots',
      href: '?section=devices',
    };
  }
  return {
    id: 'device-coverage',
    title: 'Device evidence coverage',
    state: 'unknown',
    summary: devices.linkedDevicesTruncated
      ? `Only ${devices.linkedDevices.length} of ${devices.totalLinkedDeviceCount} linked devices are shown.`
      : 'Device relationship evidence is incomplete or unavailable.',
    evidence: devices.explanation,
    source: 'WEC Inventory latest snapshots',
    href: '?section=devices',
  };
}

function strongestRelationship(device: UserLinkedDeviceProfile) {
  return [...device.relationshipEvidence].sort((left, right) => {
    const confidenceDifference = (right.confidence === 'HIGH' ? 1 : 0) - (left.confidence === 'HIGH' ? 1 : 0);
    if (confidenceDifference !== 0) return confidenceDifference;
    return Date.parse(right.observedAtUtc) - Date.parse(left.observedAtUtc);
  })[0];
}

function deviceReturnEvidence(device: UserLinkedDeviceProfile): LeaverDeviceReturnEvidence {
  const strongest = strongestRelationship(device);
  return {
    host: device.host,
    relationship: strongest ? relationshipLabels[strongest.relationshipType] : 'Unknown association',
    confidence: strongest?.confidence === 'HIGH' ? 'high' : 'medium',
    observedAtUtc: strongest?.observedAtUtc ?? device.inventoryCapturedAtUtc,
    returnState: 'unresolved',
    explanation: strongest?.explanation
      ?? 'No individual relationship observation is available; physical return remains unresolved.',
    href: `/clients/${encodeURIComponent(device.host)}`,
  };
}

export function buildLeaverAssessment(profile: UserProfileResult): LeaverAssessment {
  const items = [
    accountItem(profile),
    activityItem(profile),
    directGroupsItem(profile),
    privilegedItem(profile),
    deviceReturnItem(profile),
    deviceCoverageItem(profile),
  ];
  return {
    userObjectId: profile.identity.objectId,
    userDisplayName: profile.identity.displayName,
    accountName: profile.identity.samAccountName ?? profile.identity.userPrincipalName,
    items,
    devices: profile.devices.linkedDevices.map(deviceReturnEvidence),
    attentionCount: items.filter((item) => item.state === 'attention').length,
    unknownCount: items.filter((item) => item.state === 'unknown').length,
  };
}
