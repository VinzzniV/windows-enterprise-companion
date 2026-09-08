import type { HostProbe, HygieneStatus } from '../../shared/api-types';
import { SemanticStatusBadge, type SemanticStatus } from '../../shared/ui/SemanticStatusBadge';

export interface ClientStatusPresentation {
  status: SemanticStatus;
  context: string | null;
}

export type ClientConnectivityState =
  | { kind: 'loading' }
  | { kind: 'failed' }
  | { kind: 'unavailable' }
  | { kind: 'loaded'; probe: HostProbe };

export function clientConnectivityStatus(state: ClientConnectivityState): ClientStatusPresentation {
  switch (state.kind) {
    case 'loading':
      return { status: { dimension: 'execution', value: 'running' }, context: 'Connectivity check' };
    case 'failed':
      return { status: { dimension: 'execution', value: 'failed' }, context: 'Connectivity check' };
    case 'unavailable':
      return { status: { dimension: 'availability', value: 'unknown' }, context: 'No probe result' };
    case 'loaded':
      if (state.probe.reachable && state.probe.manageable) {
        return { status: { dimension: 'availability', value: 'available' }, context: 'Ping + WinRM 5985' };
      }
      if (state.probe.reachable) {
        return { status: { dimension: 'availability', value: 'available' }, context: 'Ping response · No WinRM response' };
      }
      if (state.probe.manageable) {
        return { status: { dimension: 'availability', value: 'available' }, context: 'WinRM 5985 open · No ping response' };
      }
      return { status: { dimension: 'availability', value: 'unknown' }, context: 'No ping or WinRM response' };
  }
}

export function hygieneAssessmentStatus(status: HygieneStatus | null): ClientStatusPresentation {
  switch (status) {
    case 'HEALTHY': return { status: { dimension: 'health', value: 'healthy' }, context: null };
    case 'WARNING': return { status: { dimension: 'health', value: 'warning' }, context: null };
    case 'CLEANUP_CANDIDATE': return { status: { dimension: 'health', value: 'critical' }, context: 'Cleanup candidate' };
    case 'CRITICAL': return { status: { dimension: 'health', value: 'critical' }, context: null };
    case 'INCOMPLETE': return { status: { dimension: 'execution', value: 'partial' }, context: 'Assessment incomplete' };
    case null: return { status: { dimension: 'availability', value: 'unknown' }, context: 'Unmanaged' };
  }
}

export function sourcePresenceStatus(present: boolean, missingApplies: boolean): SemanticStatus {
  if (present) return { dimension: 'availability', value: 'available' };
  return missingApplies
    ? { dimension: 'availability', value: 'missing' }
    : { dimension: 'availability', value: 'not-applicable' };
}

export function sourceFreshnessStatus(stale: boolean, timestamp: string | null): SemanticStatus {
  if (stale) return { dimension: 'freshness', value: 'stale' };
  return timestamp
    ? { dimension: 'freshness', value: 'fresh' }
    : { dimension: 'availability', value: 'unknown' };
}

export function snapshotAvailabilityStatus(available: boolean): SemanticStatus {
  return { dimension: 'availability', value: available ? 'available' : 'missing' };
}

export function ClientSemanticStatus({
  status,
  context = null,
}: {
  status: SemanticStatus;
  context?: string | null;
}) {
  return <span className="inline-flex items-center gap-1.5 whitespace-nowrap">
    <SemanticStatusBadge status={status} />
    {context && <span className="text-xs text-slate-400">{context}</span>}
  </span>;
}
