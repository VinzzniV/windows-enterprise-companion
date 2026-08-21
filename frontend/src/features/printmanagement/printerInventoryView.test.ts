import { describe, expect, it } from 'vitest';
import type { NetworkPolicyResult } from '../../shared/api-types';
import { buildPrinterInventoryView } from './printerInventoryView';
import type { MergedPrinter } from './printers';

function printer(overrides: Partial<MergedPrinter> & Pick<MergedPrinter, 'key' | 'name'>): MergedPrinter {
  return {
    site: 'Other',
    servers: ['PRINT01'],
    queues: [{
      server: 'PRINT01',
      queueName: overrides.name,
      shareName: overrides.name,
      driverName: null,
      driverVersion: null,
      portName: null,
      portAddress: null,
    }],
    model: null,
    serialNumber: null,
    deviceAddress: null,
    deviceIp: null,
    location: null,
    deviceLocation: null,
    serverLocation: null,
    deviceAnswered: false,
    deviceDataFromUtc: null,
    status: null,
    supplies: [],
    deviceError: null,
    ...overrides,
  };
}

const policy: NetworkPolicyResult = {
  printerSubnets: ['172.20.20.0/24'],
  legacySubnets: ['192.168.20.0/24'],
  dhcpServer: 'DHCP01',
};

const printers: MergedPrinter[] = [
  printer({
    key: 'legacy',
    name: 'PK-NETPRT002',
    site: 'PK',
    model: 'Zeta',
    deviceAddress: '192.168.20.2',
    deviceIp: '192.168.20.2',
    queues: [
      {
        server: 'PRINT01',
        queueName: 'PK-NETPRT002',
        shareName: 'PK-NETPRT002',
        driverName: null,
        driverVersion: null,
        portName: 'IP_192.168.20.2',
        portAddress: '192.168.20.2',
      },
      {
        server: 'PRINT01',
        queueName: 'PK-NETPRT002_B',
        shareName: 'PK-NETPRT002_B',
        driverName: null,
        driverVersion: null,
        portName: 'IP_192.168.20.2',
        portAddress: '192.168.20.2',
      },
    ],
    supplies: [{ description: 'Black', percent: 5, isLow: true }],
  }),
  printer({
    key: 'target',
    name: 'KF-NETPRT001',
    site: 'KF',
    model: 'Alpha',
    location: 'Floor 2',
    deviceAddress: '172.20.20.1',
    deviceIp: '172.20.20.1',
    status: 'Idle',
    supplies: [{ description: 'Black', percent: 80, isLow: false }],
  }),
  printer({
    key: 'foreign',
    name: 'SU-NETPRT003',
    site: 'SU',
    deviceIp: '10.0.0.3',
    deviceError: { code: 'CONNECTION_TIMEOUT', message: 'No answer' },
  }),
  printer({ key: 'unknown', name: 'Reception' }),
];

describe('buildPrinterInventoryView', () => {
  it('filters before sorting and grouping while keeping fleet metrics unfiltered', () => {
    const view = buildPrinterInventoryView({
      printers,
      search: 'netprt',
      groupMode: 'site',
      sort: { key: 'Model', dir: 'asc' },
      notifications: {},
      networkPolicy: policy,
      dhcpReservations: {
        '172.20.20.1': { ip: '172.20.20.1', mac: null, name: 'KF-NETPRT001' },
      },
      dhcpCheckedIps: new Set(['172.20.20.1', '192.168.20.2']),
    });

    expect(view.filteredPrinters.map((item) => item.name)).toEqual([
      'PK-NETPRT002',
      'KF-NETPRT001',
      'SU-NETPRT003',
    ]);
    expect(view.groups.map((group) => ({
      label: group.label,
      printers: group.printers.map((item) => item.name),
    }))).toEqual([
      { label: 'KF', printers: ['KF-NETPRT001'] },
      { label: 'PK', printers: ['PK-NETPRT002'] },
      { label: 'SU', printers: ['SU-NETPRT003'] },
    ]);
    expect(view.metrics).toEqual({
      queueCount: 5,
      deviceCount: 2,
      lowTonerCount: 1,
      unreachableCount: 1,
      legacyNetCount: 1,
      dhcpEligibleCount: 3,
      dhcpCheckedCount: 2,
      unreservedCount: 1,
    });
  });

  it('uses the existing subnet order for the Network column and keeps missing values last', () => {
    const view = buildPrinterInventoryView({
      printers,
      search: '',
      groupMode: 'none',
      sort: { key: 'Network', dir: 'asc' },
      notifications: {},
      networkPolicy: policy,
      dhcpReservations: {},
      dhcpCheckedIps: new Set(),
    });

    expect(view.groups[0].printers.map((item) => item.key)).toEqual([
      'target',
      'legacy',
      'foreign',
      'unknown',
    ]);
    expect(view.metrics.dhcpEligibleCount).toBe(3);
    expect(view.metrics.dhcpCheckedCount).toBe(0);
    expect(view.metrics.unreservedCount).toBe(0);
  });
});
