import type { DeviceKind, NetworkHostRow } from '../../shared/api-types';

/**
 * ok = alive + has reservation, rogue = alive + no reservation, stale =
 * reserved but dead, up = alive (DHCP was not checked, so reservation unknown).
 */
export type HostStatus = 'ok' | 'rogue' | 'stale' | 'up';

export function hostStatus(host: NetworkHostRow, dhcpChecked: boolean): HostStatus {
  if (!host.isUp) return 'stale';
  if (!dhcpChecked) return 'up';
  return host.hasReservation ? 'ok' : 'rogue';
}

export const deviceKindLabel: Record<DeviceKind, string> = {
  PRINTER: 'Drucker',
  COMPUTER: 'Computer',
  NETWORK_DEVICE: 'Netzwerkgerät',
  UNKNOWN: 'Unbekannt',
};
