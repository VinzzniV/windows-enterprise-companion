import type { NotificationCheckStatus } from '../../shared/api-types.generated';
import {
  semanticStatusPresentation,
  type SemanticStatus,
} from '../../shared/ui/SemanticStatusBadge';
import type { SubnetClass } from './printers';

export interface PrintStatusPresentation {
  status: SemanticStatus;
  context?: string;
  technicalDetail?: string;
}

export function printerObservationStatus(
  deviceErrorCode: string | null,
  rawStatus: string | null,
): PrintStatusPresentation {
  if (deviceErrorCode) {
    return {
      status: { dimension: 'availability', value: 'unknown' },
      context: 'No response',
      technicalDetail: deviceErrorCode,
    };
  }

  switch (rawStatus) {
    case 'Idle':
      return { status: { dimension: 'execution', value: 'idle' } };
    case 'Printing':
      return { status: { dimension: 'execution', value: 'running' }, context: 'Printing' };
    case 'Warmup':
      return { status: { dimension: 'execution', value: 'running' }, context: 'Warming up' };
    case 'Other':
    case 'Unknown':
      return {
        status: { dimension: 'availability', value: 'unknown' },
        context: 'Device status unavailable',
        technicalDetail: `Provider status: ${rawStatus}`,
      };
    case null:
      return {
        status: { dimension: 'availability', value: 'unknown' },
        context: 'No device status',
      };
    default:
      return {
        status: { dimension: 'availability', value: 'unknown' },
        context: 'Unmapped device status',
        technicalDetail: `Provider status: ${rawStatus}`,
      };
  }
}

/** Canonical group label matching the primary status shown in each printer row. */
export function printerObservationGroupLabel(
  deviceErrorCode: string | null,
  rawStatus: string | null,
): string {
  return semanticStatusPresentation(printerObservationStatus(deviceErrorCode, rawStatus).status).label;
}

export function printerNetworkStatus(subnet: SubnetClass): PrintStatusPresentation {
  switch (subnet) {
    case 'target':
      return { status: { dimension: 'lifecycle', value: 'current' }, context: 'Target subnet' };
    case 'legacy':
      return { status: { dimension: 'lifecycle', value: 'pending' }, context: 'Legacy subnet' };
    case 'foreign':
      return { status: { dimension: 'health', value: 'warning' }, context: 'Foreign VLAN' };
    case 'unknown':
      return { status: { dimension: 'availability', value: 'unknown' }, context: 'Not classified' };
  }
}

export function dhcpReservationStatus(reserved: boolean): PrintStatusPresentation {
  return reserved
    ? { status: { dimension: 'availability', value: 'available' }, context: 'Reserved' }
    : { status: { dimension: 'availability', value: 'missing' }, context: 'No reservation' };
}

export function notificationConfigStatus(
  status: NotificationCheckStatus,
  failedRules: number,
  error?: string | null,
): PrintStatusPresentation {
  switch (status) {
    case 'OK':
      return { status: { dimension: 'health', value: 'healthy' }, context: 'Configured' };
    case 'WARNING':
      return {
        status: { dimension: 'health', value: 'warning' },
        context: `${failedRules} issue${failedRules === 1 ? '' : 's'}`,
      };
    case 'NOT_CHECKED':
      return {
        status: { dimension: 'availability', value: 'unknown' },
        context: 'Not checked',
        ...(error ? { technicalDetail: error } : {}),
      };
  }
}

export function portReachabilityStatus(reachable: boolean): PrintStatusPresentation {
  return reachable
    ? { status: { dimension: 'availability', value: 'available' }, context: 'Reachable' }
    : { status: { dimension: 'availability', value: 'unknown' }, context: 'No response' };
}
