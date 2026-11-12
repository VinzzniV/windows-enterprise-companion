import { useEffect, useRef, useState } from 'react';
import type {
  ClientOverviewResult,
  ClientOverviewSourceMetadata,
  HygieneDevice,
  InventorySourceState,
  ProbeHostsResponse,
} from '../../shared/api-types';
import { invoke } from '../../shared/bridge/bridgeClient';
import { RelationshipMap } from '../../shared/relationships/RelationshipMap';
import type {
  RelationshipConfidence,
  RelationshipEdge,
  RelationshipMapModel,
  RelationshipNode,
  RelationshipStatus,
} from '../../shared/relationships/relationshipModel';
import { Button } from '../../shared/ui/Button';

export type IntegrationKey = 'activeDirectory' | 'kaspersky' | 'opsi' | 'nessus';
type ClientReachability = 'unknown' | 'checking' | 'online' | 'no-response' | 'failed';

const FINDINGS: Record<IntegrationKey, { stale?: string; missing?: readonly string[] }> = {
  activeDirectory: { stale: 'STALE_AD' },
  kaspersky: { stale: 'STALE_KASPERSKY', missing: ['MISSING_KASPERSKY', 'MISSING_KASPERSKY_AGENT', 'MISSING_KES'] },
  opsi: { stale: 'STALE_OPSI', missing: ['MISSING_OPSI'] },
  nessus: { stale: 'STALE_NESSUS', missing: ['MISSING_NESSUS'] },
};

const missingMetadata = (source: string, detailSection: string): ClientOverviewSourceMetadata => ({
  source,
  provenance: 'Stored WEC evidence',
  freshness: 'UNKNOWN',
  capturedAtUtc: null,
  ageSeconds: null,
  isComplete: false,
  coverage: 'Source metadata unavailable',
  detailSection,
});

export function integrationStatus(
  key: IntegrationKey,
  source: InventorySourceState,
  present: boolean,
  findingCodes: ReadonlySet<string>,
): RelationshipStatus {
  if (source.availability === 'PARTIAL' || source.availability === 'TRUNCATED') return 'partial';
  if (source.availability !== 'AVAILABLE') return 'unknown';
  if (FINDINGS[key].stale && findingCodes.has(FINDINGS[key].stale!)) return 'stale';
  if (!present || FINDINGS[key].missing?.some((code) => findingCodes.has(code))) return 'disconnected';
  return 'connected';
}

function storedEvidenceStatus(metadata: ClientOverviewSourceMetadata): RelationshipStatus {
  if (metadata.freshness === 'MISSING') return 'disconnected';
  if (metadata.freshness === 'STALE') return 'stale';
  if (metadata.freshness === 'UNKNOWN') return 'unknown';
  return metadata.isComplete ? 'connected' : 'partial';
}

function combinedStoredEvidenceStatus(metadata: readonly ClientOverviewSourceMetadata[]): RelationshipStatus {
  const statuses = metadata.map(storedEvidenceStatus);
  if (statuses.every((status) => status === 'disconnected')) return 'disconnected';
  if (statuses.includes('unknown')) return 'unknown';
  if (statuses.includes('disconnected') || statuses.includes('partial')) return 'partial';
  if (statuses.includes('stale')) return 'stale';
  return 'connected';
}

function confidenceFor(status: RelationshipStatus, stored: boolean): RelationshipConfidence {
  if (!stored) return status === 'unknown' ? 'unknown' : 'low';
  if (status === 'connected' || status === 'stale') return stored ? 'confirmed' : 'high';
  if (status === 'disconnected') return 'medium';
  if (status === 'partial') return 'low';
  return 'unknown';
}

function relationshipEdge(
  primaryId: string,
  node: RelationshipNode,
  relationshipType: string,
  evidenceSource: string,
  explanation: string,
  stored = false,
  observedAtUtc = node.observedAtUtc,
): RelationshipEdge {
  return {
    id: `${primaryId}:${node.id}`,
    fromNodeId: primaryId,
    toNodeId: node.id,
    relationshipType,
    evidenceSource,
    observedAtUtc,
    confidence: confidenceFor(node.status, stored),
    explanation,
  };
}

function latestObservation(nodes: readonly RelationshipNode[]): string | null {
  const timestamps = nodes
    .map((node) => node.observedAtUtc ? Date.parse(node.observedAtUtc) : Number.NaN)
    .filter(Number.isFinite);
  return timestamps.length > 0 ? new Date(Math.max(...timestamps)).toISOString() : null;
}

function metadataFor(overview: ClientOverviewResult, source: string, detailSection: string): ClientOverviewSourceMetadata {
  return overview.sources.find((entry) => entry.source === source) ?? missingMetadata(source, detailSection);
}

export function buildDeviceRelationshipModel(
  device: HygieneDevice,
  sources: Record<IntegrationKey, InventorySourceState>,
  overview: ClientOverviewResult,
  reachability: ClientReachability,
  managementObservedAtUtc: string,
): RelationshipMapModel {
  const findingCodes = new Set(device.assessment.findings.map((finding) => finding.code));
  const primaryId = `device:${device.hostName.toLocaleLowerCase()}`;
  const inventoryMetadata = metadataFor(overview, 'Inventory', 'inventory');
  const softwareMetadata = metadataFor(overview, 'Installed software', 'inventory');
  const healthMetadata = metadataFor(overview, 'Health', 'diagnostics');
  const securityMetadata = metadataFor(overview, 'Security', 'security');
  const clientPath = `/clients/${encodeURIComponent(device.hostName)}`;
  const nessus = device.nessus ?? {
    exists: false,
    assetId: null,
    ipAddress: null,
    lastCompletedScanUtc: null,
    critical: 0,
    high: 0,
    medium: 0,
    low: 0,
    info: 0,
    ports: [],
    scanSources: [],
  };
  const nessusPresent = nessus.exists && nessus.lastCompletedScanUtc !== null;

  const related: RelationshipNode[] = [
    {
      id: 'management:active-directory',
      entityType: 'management-system',
      label: 'Active Directory',
      context: sources.activeDirectory.error ?? (device.activeDirectory.exists ? 'Computer object found' : 'No matching computer object'),
      status: integrationStatus('activeDirectory', sources.activeDirectory, device.activeDirectory.exists, findingCodes),
      observedAtUtc: device.activeDirectory.lastLogonDate,
      href: '/activedirectory',
    },
    {
      id: 'management:kaspersky',
      entityType: 'management-system',
      label: 'Kaspersky',
      context: sources.kaspersky.error ?? (device.kaspersky.exists ? 'Managed device found' : 'No matching managed device'),
      status: integrationStatus('kaspersky', sources.kaspersky, device.kaspersky.exists, findingCodes),
      observedAtUtc: device.kaspersky.lastSeen,
      href: '/settings?section=environment-health',
    },
    {
      id: 'management:opsi',
      entityType: 'management-system',
      label: 'opsi',
      context: sources.opsi.error ?? (device.opsi.exists ? 'Managed client found' : 'No matching managed client'),
      status: integrationStatus('opsi', sources.opsi, device.opsi.exists, findingCodes),
      observedAtUtc: device.opsi.lastSeen,
      href: '/patchmanagement',
    },
    {
      id: 'management:nessus',
      entityType: 'management-system',
      label: 'Nessus',
      context: sources.nessus.error ?? (nessusPresent ? `${nessus.critical} critical · ${nessus.high} high` : 'No completed asset scan'),
      status: integrationStatus('nessus', sources.nessus, nessusPresent, findingCodes),
      observedAtUtc: nessus.lastCompletedScanUtc,
      href: `/vulnerabilities?tab=findings&asset=${encodeURIComponent(device.computerName)}`,
    },
    {
      id: 'wec:inventory',
      entityType: 'inventory',
      label: 'WEC Inventory',
      context: overview.inventory
        ? `${overview.inventory.operatingSystem} · ${overview.software?.installedCount ?? 0} installed applications`
        : 'No stored hardware snapshot',
      status: combinedStoredEvidenceStatus([inventoryMetadata, softwareMetadata]),
      observedAtUtc: inventoryMetadata.capturedAtUtc,
      href: `${clientPath}?section=inventory`,
    },
    {
      id: 'wec:health',
      entityType: 'health',
      label: 'Device Health',
      context: overview.health
        ? `${overview.health.criticalCount} critical · ${overview.health.warningCount} warning · ${overview.health.healthyCount} passed`
        : 'No stored Health run',
      status: storedEvidenceStatus(healthMetadata),
      observedAtUtc: healthMetadata.capturedAtUtc,
      href: `${clientPath}?section=diagnostics`,
    },
    {
      id: 'wec:security',
      entityType: 'security',
      label: 'Security posture',
      context: overview.security
        ? `${overview.security.criticalCount} critical · ${overview.security.highCount} high · ${overview.security.mediumCount} medium`
        : 'No stored Security scan',
      status: storedEvidenceStatus(securityMetadata),
      observedAtUtc: securityMetadata.capturedAtUtc,
      href: `${clientPath}?section=security`,
    },
  ];

  const userNodes: RelationshipNode[] = (overview.users?.observations ?? []).map((observation) => ({
    id: `user:${observation.directorySid.toLocaleLowerCase()}`,
    entityType: 'user',
    label: observation.accountDisplay,
    context: `${observation.relationshipType.toLocaleLowerCase().replaceAll('_', ' ')} · ${observation.confidence.toLocaleLowerCase()} confidence`,
    status: observation.confidence === 'HIGH' ? 'connected' : 'partial',
    observedAtUtc: observation.observedAtUtc,
  }));
  related.push(...userNodes);

  const primaryStatus: RelationshipStatus = reachability === 'online'
    ? 'connected'
    : reachability === 'failed'
      ? 'partial'
      : 'unknown';
  const reachabilityContext = reachability === 'online'
    ? 'Ping or WinRM responded'
    : reachability === 'no-response'
      ? 'No ping or WinRM response'
      : reachability === 'checking'
        ? 'Checking connectivity'
        : reachability === 'failed'
          ? 'Connectivity check failed'
          : 'Connectivity not checked';
  const primary: RelationshipNode = {
    id: primaryId,
    entityType: 'device',
    label: device.computerName,
    context: `${device.hostName} · ${reachabilityContext}`,
    status: primaryStatus,
    observedAtUtc: latestObservation(related),
    href: clientPath,
  };

  const edges: RelationshipEdge[] = [
    relationshipEdge(primaryId, related[0], 'Name candidate in', 'Active Directory computer inventory', device.correlationExplanation ?? 'Directory name candidate; no shared device identity is established.', false, managementObservedAtUtc),
    relationshipEdge(primaryId, related[1], 'Name candidate in', 'Kaspersky managed-device inventory', device.correlationExplanation ?? 'Kaspersky name candidate; no shared device identity is established.', false, managementObservedAtUtc),
    relationshipEdge(primaryId, related[2], 'Name candidate in', 'opsi client inventory', device.correlationExplanation ?? 'opsi name candidate; no shared device identity is established.', false, managementObservedAtUtc),
    relationshipEdge(primaryId, related[3], 'Name candidate in', 'Nessus completed-scan inventory', device.correlationExplanation ?? 'Nessus name candidate; no shared device identity is established.', false, managementObservedAtUtc),
    relationshipEdge(primaryId, related[4], 'Described by', inventoryMetadata.provenance, inventoryMetadata.coverage, true),
    relationshipEdge(primaryId, related[5], 'Observed by', healthMetadata.provenance, healthMetadata.coverage, true),
    relationshipEdge(primaryId, related[6], 'Assessed by', securityMetadata.provenance, securityMetadata.coverage, true),
    ...userNodes.map((node, index): RelationshipEdge => {
      const observation = overview.users!.observations[index];
      return {
        id: `${primaryId}:${node.id}`,
        fromNodeId: primaryId,
        toNodeId: node.id,
        relationshipType: observation.relationshipType.toLocaleLowerCase().replaceAll('_', ' '),
        evidenceSource: observation.source,
        observedAtUtc: observation.observedAtUtc,
        confidence: observation.confidence === 'HIGH' ? 'high' : 'medium',
        explanation: observation.explanation,
      };
    }),
  ];

  return {
    title: 'Device relationships',
    description: 'Stored WEC evidence and explicitly loaded management-source matches. Relationships are observations, not ownership claims.',
    primaryNodeId: primaryId,
    nodes: [primary, ...related],
    edges,
  };
}

export function DeviceRelationshipMap({
  device,
  sources,
  overview,
  managementObservedAtUtc,
}: {
  device: HygieneDevice;
  sources: Record<IntegrationKey, InventorySourceState>;
  overview: ClientOverviewResult;
  managementObservedAtUtc: string;
}) {
  const probeRequestRef = useRef(0);
  const [reachability, setReachability] = useState<ClientReachability>('unknown');

  useEffect(() => {
    probeRequestRef.current += 1;
    setReachability('unknown');
  }, [device.hostName, device.evidenceKey, device.canTargetWindows]);

  const checkConnectivity = () => {
    if (device.canTargetWindows === false) return;
    const request = ++probeRequestRef.current;
    setReachability('checking');
    void invoke<ProbeHostsResponse>('connectivity', 'probeHosts', { hosts: [device.hostName] })
      .then((response) => {
        if (probeRequestRef.current !== request) return;
        const result = response.results.find(row => row.host.toUpperCase() === device.hostName.toUpperCase());
        setReachability(result?.reachable || result?.manageable ? 'online' : 'no-response');
      })
      .catch(() => { if (probeRequestRef.current === request) setReachability('failed'); });
  };

  const reachabilityLabel = reachability === 'online'
    ? 'Ping or WinRM responded'
    : reachability === 'no-response'
      ? 'No ping or WinRM response'
      : reachability === 'checking'
        ? 'Checking…'
        : reachability === 'failed'
          ? 'Check failed'
          : 'Not checked';
  const model = buildDeviceRelationshipModel(device, sources, overview, reachability, managementObservedAtUtc);

  return <RelationshipMap model={model} actions={<>
    <span className="text-xs text-slate-500" role="status">Connectivity: {reachabilityLabel}</span>
    <Button variant="ghost" disabled={device.canTargetWindows === false || reachability === 'checking'} onClick={checkConnectivity}>
      {reachability === 'checking' ? 'Checking …' : 'Check connectivity'}
    </Button>
  </>} />;
}
