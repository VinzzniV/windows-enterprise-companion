import type { DeviceKind, NetworkHostRow } from '../../shared/api-types';
import type { SemanticStatus } from '../../shared/ui/SemanticStatusBadge';

/**
 * ok = alive + has reservation, rogue = alive + no reservation, stale =
 * reserved but dead, up = alive (DHCP was not checked, so reservation unknown).
 */
export type HostStatus = 'ok' | 'rogue' | 'stale' | 'up';

export interface HostStatusPresentation {
  status: SemanticStatus;
  context: string;
}

const hostStatusPresentations = {
  ok: { status: { dimension: 'availability', value: 'available' }, context: 'Reserved' },
  rogue: { status: { dimension: 'availability', value: 'missing' }, context: 'No reservation' },
  stale: { status: { dimension: 'freshness', value: 'stale' }, context: 'Reservation did not answer' },
  up: { status: { dimension: 'availability', value: 'available' }, context: 'Active' },
} as const satisfies Record<HostStatus, HostStatusPresentation>;

export function hostStatus(host: NetworkHostRow, dhcpChecked: boolean): HostStatus {
  if (!host.isUp) return 'stale';
  if (!dhcpChecked) return 'up';
  return host.hasReservation ? 'ok' : 'rogue';
}

export function hostStatusPresentation(status: HostStatus): HostStatusPresentation {
  return hostStatusPresentations[status];
}

export const deviceKindLabel: Record<DeviceKind, string> = {
  PRINTER: 'Printer',
  COMPUTER: 'Computer',
  NETWORK_DEVICE: 'Network device',
  UNKNOWN: 'Unknown',
};
