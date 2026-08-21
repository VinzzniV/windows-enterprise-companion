import type {
  HygieneSourceProgressStatus,
  InventorySourceAvailability,
} from '../api-types';
import type { SemanticStatus } from '../ui/SemanticStatusBadge';

export interface InventorySourceStatusPresentation {
  status: SemanticStatus;
  context: string | null;
}

const inventorySourceStatuses = {
  AVAILABLE: { status: { dimension: 'availability', value: 'available' }, context: null },
  NOT_CONNECTED: { status: { dimension: 'availability', value: 'not-configured' }, context: 'Not connected' },
  UNAVAILABLE: { status: { dimension: 'execution', value: 'failed' }, context: 'Source unavailable' },
  TRUNCATED: { status: { dimension: 'execution', value: 'partial' }, context: 'Result truncated' },
  PARTIAL: { status: { dimension: 'execution', value: 'partial' }, context: null },
} as const satisfies Record<InventorySourceAvailability, InventorySourceStatusPresentation>;

const progressStatuses = {
  RUNNING: { status: { dimension: 'execution', value: 'running' }, context: null },
  ...inventorySourceStatuses,
} as const satisfies Record<HygieneSourceProgressStatus, InventorySourceStatusPresentation>;

export function inventorySourceStatus(
  availability: InventorySourceAvailability,
): InventorySourceStatusPresentation {
  return inventorySourceStatuses[availability];
}

export function hygieneSourceProgressStatus(
  progress: HygieneSourceProgressStatus,
): InventorySourceStatusPresentation {
  return progressStatuses[progress];
}
