import { describe, expect, it } from 'vitest';
import { hostStatus } from './network';
import type { NetworkHostRow } from '../../shared/api-types';

const base: NetworkHostRow = {
  ip: '172.20.20.10',
  hostname: null,
  isUp: true,
  kind: 'UNKNOWN',
  openPorts: [],
  macAddress: null,
  macVendor: null,
  hasReservation: false,
  reservationName: null,
};

describe('hostStatus', () => {
  it('alive with reservation is ok', () => {
    expect(hostStatus({ ...base, hasReservation: true }, true)).toBe('ok');
  });

  it('alive without reservation (checked) is rogue', () => {
    expect(hostStatus(base, true)).toBe('rogue');
  });

  it('reserved but dead is stale', () => {
    expect(hostStatus({ ...base, isUp: false, hasReservation: true }, true)).toBe('stale');
  });

  it('alive without a DHCP check is just up', () => {
    expect(hostStatus(base, false)).toBe('up');
  });
});
