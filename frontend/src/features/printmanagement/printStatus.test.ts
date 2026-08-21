import { describe, expect, it } from 'vitest';
import {
  dhcpReservationStatus,
  notificationConfigStatus,
  portReachabilityStatus,
  printerNetworkStatus,
  printerObservationStatus,
} from './printStatus';

describe('Print Management semantic status mappings', () => {
  it.each([
    [null, 'Idle', { status: { dimension: 'execution', value: 'idle' } }],
    [null, 'Printing', { status: { dimension: 'execution', value: 'running' }, context: 'Printing' }],
    [null, 'Warmup', { status: { dimension: 'execution', value: 'running' }, context: 'Warming up' }],
    ['CONNECTION_TIMEOUT', null, {
      status: { dimension: 'availability', value: 'unknown' },
      context: 'No response',
      technicalDetail: 'CONNECTION_TIMEOUT',
    }],
    [null, 'Other', {
      status: { dimension: 'availability', value: 'unknown' },
      context: 'Device status unavailable',
      technicalDetail: 'Provider status: Other',
    }],
    [null, null, {
      status: { dimension: 'availability', value: 'unknown' },
      context: 'No device status',
    }],
  ] as const)('maps device error %s and provider status %s without guessing', (error, rawStatus, expected) => {
    expect(printerObservationStatus(error, rawStatus)).toEqual(expected);
  });

  it.each([
    ['target', { status: { dimension: 'lifecycle', value: 'current' }, context: 'Target subnet' }],
    ['legacy', { status: { dimension: 'lifecycle', value: 'pending' }, context: 'Legacy subnet' }],
    ['foreign', { status: { dimension: 'health', value: 'warning' }, context: 'Foreign VLAN' }],
    ['unknown', { status: { dimension: 'availability', value: 'unknown' }, context: 'Not classified' }],
  ] as const)('maps %s network policy semantics', (subnet, expected) => {
    expect(printerNetworkStatus(subnet)).toEqual(expected);
  });

  it('keeps DHCP absence distinct from a failed execution', () => {
    expect(dhcpReservationStatus(true)).toEqual({
      status: { dimension: 'availability', value: 'available' },
      context: 'Reserved',
    });
    expect(dhcpReservationStatus(false)).toEqual({
      status: { dimension: 'availability', value: 'missing' },
      context: 'No reservation',
    });
  });

  it('maps notification health and an unverified check independently', () => {
    expect(notificationConfigStatus('OK', 0)).toEqual({
      status: { dimension: 'health', value: 'healthy' },
      context: 'Configured',
    });
    expect(notificationConfigStatus('WARNING', 2)).toEqual({
      status: { dimension: 'health', value: 'warning' },
      context: '2 issues',
    });
    expect(notificationConfigStatus('NOT_CHECKED', 0, 'Login refused')).toEqual({
      status: { dimension: 'availability', value: 'unknown' },
      context: 'Not checked',
      technicalDetail: 'Login refused',
    });
  });

  it('does not claim a host is missing when ping returns no response', () => {
    expect(portReachabilityStatus(true)).toEqual({
      status: { dimension: 'availability', value: 'available' },
      context: 'Reachable',
    });
    expect(portReachabilityStatus(false)).toEqual({
      status: { dimension: 'availability', value: 'unknown' },
      context: 'No response',
    });
  });
});
