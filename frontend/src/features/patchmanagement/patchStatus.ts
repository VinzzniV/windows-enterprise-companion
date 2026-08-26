import type { PatchPackageStatus, PatchWorkflowState } from '../../shared/api-types';
import type { SemanticStatus } from '../../shared/ui/SemanticStatusBadge';

export interface PatchStatusPresentation {
  status: SemanticStatus;
  context: string | null;
  technicalDetail: string | null;
}

export type OpsiConnectionStatusState =
  | { kind: 'loading' }
  | { kind: 'failed' }
  | { kind: 'unavailable' }
  | { kind: 'loaded'; status: { connected: boolean } };

export function opsiConnectionStatus(state: OpsiConnectionStatusState): PatchStatusPresentation {
  if (state.kind === 'loading') return { status: { dimension: 'execution', value: 'running' }, context: 'opsi connection', technicalDetail: null };
  if (state.kind === 'failed') return { status: { dimension: 'execution', value: 'failed' }, context: 'Connection check', technicalDetail: null };
  if (state.kind === 'unavailable') return { status: { dimension: 'availability', value: 'unknown' }, context: 'Connection status unavailable', technicalDetail: null };
  return state.status.connected
    ? { status: { dimension: 'availability', value: 'available' }, context: 'opsi connection', technicalDetail: null }
    : { status: { dimension: 'availability', value: 'unknown' }, context: 'Not connected', technicalDetail: null };
}

export function patchAuditResultStatus(rawResult: string): PatchStatusPresentation {
  if (rawResult === 'SUCCESS') return { status: { dimension: 'execution', value: 'succeeded' }, context: null, technicalDetail: null };
  if (rawResult === 'FAILED') return { status: { dimension: 'execution', value: 'failed' }, context: null, technicalDetail: null };
  if (rawResult === 'PLANNED') return { status: { dimension: 'execution', value: 'succeeded' }, context: 'Preview created', technicalDetail: null };
  return { status: { dimension: 'availability', value: 'unknown' }, context: 'Audit result unavailable', technicalDetail: rawResult };
}

export function patchPackageStatus(rawStatus: PatchPackageStatus | string): PatchStatusPresentation {
  switch (rawStatus) {
    case 'CURRENT': return { status: { dimension: 'lifecycle', value: 'current' }, context: null, technicalDetail: null };
    case 'UPDATE_AVAILABLE': return { status: { dimension: 'lifecycle', value: 'update-available' }, context: null, technicalDetail: null };
    case 'DEPOT_DEVIATION': return { status: { dimension: 'health', value: 'warning' }, context: 'Depot deviation', technicalDetail: null };
    case 'MISSING_ON_DEPOT': return { status: { dimension: 'availability', value: 'missing' }, context: 'From depot', technicalDetail: null };
    case 'CHECK_FAILED': return { status: { dimension: 'execution', value: 'failed' }, context: 'Package check', technicalDetail: null };
    case 'ACTION_PENDING': return { status: { dimension: 'lifecycle', value: 'pending' }, context: 'opsi action pending', technicalDetail: null };
    default: return { status: { dimension: 'availability', value: 'unknown' }, context: 'Package status unavailable', technicalDetail: rawStatus };
  }
}

export function patchWorkflowStatus(rawStatus: PatchWorkflowState | string): PatchStatusPresentation {
  switch (rawStatus) {
    case 'DETECTED': return { status: { dimension: 'lifecycle', value: 'pending' }, context: 'Not installed', technicalDetail: null };
    case 'UPDATE_AVAILABLE': return { status: { dimension: 'lifecycle', value: 'update-available' }, context: null, technicalDetail: null };
    case 'ACTION_PENDING': return { status: { dimension: 'lifecycle', value: 'pending' }, context: 'opsi action pending', technicalDetail: null };
    case 'COMPLETED': return { status: { dimension: 'lifecycle', value: 'current' }, context: null, technicalDetail: null };
    case 'FAILED': return { status: { dimension: 'execution', value: 'failed' }, context: null, technicalDetail: null };
    default: return { status: { dimension: 'availability', value: 'unknown' }, context: 'Client state', technicalDetail: rawStatus };
  }
}
