import type {
  PackageWorkflowStatus,
  PatchPackageStatus,
  PatchWorkflowState,
  ProductVersionSource,
} from '../../shared/api-types';
import type { SemanticStatus } from '../../shared/ui/SemanticStatusBadge';

export interface PatchStatusPresentation {
  status: SemanticStatus;
  context: string | null;
  technicalDetail: string | null;
}

export type PackageApprovalStatusState =
  | { kind: 'loading' }
  | { kind: 'failed' }
  | { kind: 'unavailable' }
  | {
      kind: 'loaded';
      workflow: Pick<PackageWorkflowStatus, 'pilotApproved' | 'testUpdateSucceededAtUtc'>;
    };

export type OpsiConnectionStatusState =
  | { kind: 'loading' }
  | { kind: 'failed' }
  | { kind: 'unavailable' }
  | { kind: 'loaded'; status: { connected: boolean } };

export function opsiConnectionStatus(
  state: OpsiConnectionStatusState,
): PatchStatusPresentation {
  switch (state.kind) {
    case 'loading':
      return {
        status: { dimension: 'execution', value: 'running' },
        context: 'opsi connection',
        technicalDetail: null,
      };
    case 'failed':
      return {
        status: { dimension: 'execution', value: 'failed' },
        context: 'Connection check',
        technicalDetail: null,
      };
    case 'unavailable':
      return {
        status: { dimension: 'availability', value: 'unknown' },
        context: 'Connection status unavailable',
        technicalDetail: null,
      };
    case 'loaded':
      return state.status.connected
        ? {
            status: { dimension: 'availability', value: 'available' },
            context: 'opsi connection',
            technicalDetail: null,
          }
        : {
            status: { dimension: 'availability', value: 'unknown' },
            context: 'Not connected',
            technicalDetail: null,
          };
  }
}

export function patchAuditResultStatus(rawResult: string): PatchStatusPresentation {
  switch (rawResult) {
    case 'SUCCESS':
      return {
        status: { dimension: 'execution', value: 'succeeded' },
        context: null,
        technicalDetail: null,
      };
    case 'FAILED':
      return {
        status: { dimension: 'execution', value: 'failed' },
        context: null,
        technicalDetail: null,
      };
    case 'PLANNED':
      return {
        status: { dimension: 'execution', value: 'succeeded' },
        context: 'Preview created',
        technicalDetail: null,
      };
    default:
      return {
        status: { dimension: 'availability', value: 'unknown' },
        context: 'Audit result unavailable',
        technicalDetail: rawResult,
      };
  }
}

export function packageApprovalStatus(
  state: PackageApprovalStatusState,
): PatchStatusPresentation {
  switch (state.kind) {
    case 'loading':
      return {
        status: { dimension: 'execution', value: 'running' },
        context: 'Approval status',
        technicalDetail: null,
      };
    case 'failed':
      return {
        status: { dimension: 'execution', value: 'failed' },
        context: 'Approval status',
        technicalDetail: null,
      };
    case 'unavailable':
      return {
        status: { dimension: 'availability', value: 'unknown' },
        context: 'Approval status unavailable',
        technicalDetail: null,
      };
    case 'loaded':
      if (state.workflow.pilotApproved) {
        return {
          status: { dimension: 'execution', value: 'succeeded' },
          context: 'Pilot approved',
          technicalDetail: null,
        };
      }
      if (state.workflow.testUpdateSucceededAtUtc) {
        return {
          status: { dimension: 'execution', value: 'succeeded' },
          context: 'Test update',
          technicalDetail: null,
        };
      }
      return {
        status: { dimension: 'lifecycle', value: 'pending' },
        context: 'Test update',
        technicalDetail: null,
      };
  }
}

export function patchPackageStatus(rawStatus: PatchPackageStatus | string): PatchStatusPresentation {
  switch (rawStatus) {
    case 'CURRENT':
      return {
        status: { dimension: 'lifecycle', value: 'current' },
        context: null,
        technicalDetail: null,
      };
    case 'UPDATE_AVAILABLE':
      return {
        status: { dimension: 'lifecycle', value: 'update-available' },
        context: null,
        technicalDetail: null,
      };
    case 'DEPOT_DEVIATION':
      return {
        status: { dimension: 'health', value: 'warning' },
        context: 'Depot deviation',
        technicalDetail: null,
      };
    case 'MISSING_ON_DEPOT':
      return {
        status: { dimension: 'availability', value: 'missing' },
        context: 'From depot',
        technicalDetail: null,
      };
    case 'CHECK_FAILED':
      return {
        status: { dimension: 'execution', value: 'failed' },
        context: 'Package check',
        technicalDetail: null,
      };
    case 'DEPLOYMENT_PENDING':
      return {
        status: { dimension: 'lifecycle', value: 'pending' },
        context: 'Deployment',
        technicalDetail: null,
      };
    default:
      return {
        status: { dimension: 'availability', value: 'unknown' },
        context: 'Package status unavailable',
        technicalDetail: rawStatus,
      };
  }
}

export function patchWorkflowStatus(rawStatus: PatchWorkflowState | string): PatchStatusPresentation {
  switch (rawStatus) {
    case 'DETECTED':
      return {
        status: { dimension: 'lifecycle', value: 'pending' },
        context: 'Detected',
        technicalDetail: null,
      };
    case 'UPDATE_AVAILABLE':
      return {
        status: { dimension: 'lifecycle', value: 'update-available' },
        context: null,
        technicalDetail: null,
      };
    case 'DOWNLOAD_NEEDED':
      return {
        status: { dimension: 'lifecycle', value: 'pending' },
        context: 'Download required',
        technicalDetail: null,
      };
    case 'PACKAGE_PREPARED':
      return {
        status: { dimension: 'lifecycle', value: 'pending' },
        context: 'Package prepared',
        technicalDetail: null,
      };
    case 'UPLOADED':
      return {
        status: { dimension: 'lifecycle', value: 'pending' },
        context: 'Uploaded',
        technicalDetail: null,
      };
    case 'READY_FOR_PILOT':
      return {
        status: { dimension: 'lifecycle', value: 'pending' },
        context: 'Ready for pilot',
        technicalDetail: null,
      };
    case 'APPROVED':
      return {
        status: { dimension: 'lifecycle', value: 'pending' },
        context: 'Approved',
        technicalDetail: null,
      };
    case 'ROLLOUT_REQUESTED':
      return {
        status: { dimension: 'lifecycle', value: 'pending' },
        context: 'Deployment requested',
        technicalDetail: null,
      };
    case 'COMPLETED':
      return {
        status: { dimension: 'lifecycle', value: 'current' },
        context: null,
        technicalDetail: null,
      };
    case 'FAILED':
      return {
        status: { dimension: 'execution', value: 'failed' },
        context: null,
        technicalDetail: null,
      };
    default:
      return {
        status: { dimension: 'availability', value: 'unknown' },
        context: 'Workflow status unavailable',
        technicalDetail: rawStatus,
      };
  }
}

export function manufacturerCheckStatus(
  rawStatus: string,
  enabled = true,
): PatchStatusPresentation {
  if (!enabled) {
    return {
      status: { dimension: 'lifecycle', value: 'disabled' },
      context: null,
      technicalDetail: null,
    };
  }

  switch (rawStatus) {
    case 'SUCCESS':
      return {
        status: { dimension: 'execution', value: 'succeeded' },
        context: null,
        technicalDetail: null,
      };
    case 'FAILED':
      return {
        status: { dimension: 'execution', value: 'failed' },
        context: null,
        technicalDetail: null,
      };
    case 'NOT_CHECKED':
      return {
        status: { dimension: 'availability', value: 'unknown' },
        context: 'Not checked',
        technicalDetail: null,
      };
    case 'NOT_CONFIGURED':
      return {
        status: { dimension: 'availability', value: 'not-configured' },
        context: null,
        technicalDetail: null,
      };
    default:
      return {
        status: { dimension: 'availability', value: 'unknown' },
        context: 'Check status unavailable',
        technicalDetail: rawStatus,
      };
  }
}

export function manufacturerSourcesStatus(
  sources: ReadonlyArray<Pick<ProductVersionSource, 'enabled'>>,
  loadFailed: boolean,
): PatchStatusPresentation {
  if (loadFailed) {
    return {
      status: { dimension: 'execution', value: 'failed' },
      context: 'Sources unavailable',
      technicalDetail: null,
    };
  }

  const activeCount = sources.filter((source) => source.enabled).length;
  if (activeCount > 0) {
    return {
      status: { dimension: 'availability', value: 'available' },
      context: `${activeCount} active`,
      technicalDetail: null,
    };
  }

  if (sources.length > 0) {
    return {
      status: { dimension: 'lifecycle', value: 'disabled' },
      context: `${sources.length} configured`,
      technicalDetail: null,
    };
  }

  return {
    status: { dimension: 'availability', value: 'not-configured' },
    context: null,
    technicalDetail: null,
  };
}
