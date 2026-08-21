import { describe, expect, it } from 'vitest';
import type { PrinterEntry } from '../../shared/api-types';
import type { NetworkPolicyResult } from '../../shared/api-types';
import {
  baseQueueName,
  classifySubnet,
  displayedLocation,
  filterPrinters,
  groupPrinters,
  ipInCidr,
  locationFlag,
  lowestTonerPercent,
  mergePrinters,
  siteOf,
  type ServerEntry,
} from './printers';

function entry(over: Partial<PrinterEntry>): PrinterEntry {
  return {
    queueName: 'Q',
    shareName: null,
    driverName: null,
    driverVersion: null,
    portName: null,
    deviceAddress: null,
    deviceIp: null,
    location: null,
    comment: null,
    device: null,
    deviceError: null,
    deviceDataFromUtc: null,
    ...over,
  };
}

const withDevice = (over: Partial<PrinterEntry>, device: Partial<NonNullable<PrinterEntry['device']>>) =>
  entry({
    ...over,
    device: {
      serialNumber: null,
      model: null,
      sysName: null,
      sysLocation: null,
      status: null,
      pageCount: null,
      supplies: [],
      ...device,
    },
  });

describe('baseQueueName', () => {
  it('strips variant suffixes only after a digit', () => {
    expect(baseQueueName('KF-NETPRT001B')).toBe('KF-NETPRT001');
    expect(baseQueueName('KF-NETPRT001_A5')).toBe('KF-NETPRT001');
    expect(baseQueueName('PK-NETPRT010Color')).toBe('PK-NETPRT010');
    expect(baseQueueName('MA-NETPRT007C')).toBe('MA-NETPRT007');
    expect(baseQueueName('KF-NETPRT001')).toBe('KF-NETPRT001');
    expect(baseQueueName('Reception-PDFC')).toBe('Reception-PDFC'); // C not after a digit
  });

  it('strips chained and underscore-only suffixes', () => {
    expect(baseQueueName('PK-NETPRT002_B_PCL')).toBe('PK-NETPRT002');
    expect(baseQueueName('PK-NETPRT036_2')).toBe('PK-NETPRT036');
    expect(baseQueueName('PK-NETPRT041_Q')).toBe('PK-NETPRT041');
    expect(baseQueueName('PK-NETPRT018_A5')).toBe('PK-NETPRT018');
    // Not a variant token — a real queue name keeps its suffix
    expect(baseQueueName('CNIPF770_KST')).toBe('CNIPF770_KST');
    expect(baseQueueName('Magicard 300 (V2)')).toBe('Magicard 300 (V2)');
  });
});

describe('siteOf', () => {
  it('reads the two-letter site prefix', () => {
    expect(siteOf('KF-NETPRT001')).toBe('KF');
    expect(siteOf('NETPRT001')).toBe('Other');
  });
});

describe('mergePrinters', () => {
  it('merges queues of one device by serial, then IP, then base name', () => {
    const entries: ServerEntry[] = [
      { server: 'PRSRV', entry: withDevice({ queueName: 'KF-NETPRT001B', deviceAddress: '10.0.0.1' }, { serialNumber: 'S1', model: 'UTAX', supplies: [{ description: 'Black', percent: 5, isLow: true }] }) },
      { server: 'PRSRV', entry: withDevice({ queueName: 'KF-NETPRT001C', deviceAddress: '10.0.0.1' }, { serialNumber: 'S1', model: 'UTAX' }) },
      // same base name, no serial/ip → merges by name
      { server: 'PRSRV', entry: entry({ queueName: 'KF-NETPRT002_A5' }) },
      { server: 'PRSRV', entry: entry({ queueName: 'KF-NETPRT002' }) },
      // different device
      { server: 'PRSRV', entry: withDevice({ queueName: 'PK-NETPRT003' }, { serialNumber: 'S2', model: 'Kyocera' }) },
    ];
    const merged = mergePrinters(entries);
    expect(merged).toHaveLength(3);

    const first = merged.find((p) => p.serialNumber === 'S1')!;
    expect(first.name).toBe('KF-NETPRT001');
    expect(first.queues.map((q) => q.queueName).sort()).toEqual(['KF-NETPRT001B', 'KF-NETPRT001C']);
    expect(first.site).toBe('KF');
    expect(first.model).toBe('UTAX');

    const byName = merged.find((p) => p.name === 'KF-NETPRT002')!;
    expect(byName.queues).toHaveLength(2);
  });

  it('falls back to SNMP sysLocation when the queue location is a blank string', () => {
    const merged = mergePrinters([
      { server: 'PRSRV', entry: withDevice({ queueName: 'KF-NETPRT005', location: '' }, { serialNumber: 'S5', sysLocation: 'Floor 2' }) },
    ]);
    expect(merged[0].location).toBe('Floor 2');
  });

  it('keeps the device error when no queue reached the device', () => {
    const merged = mergePrinters([
      { server: 'PRSRV', entry: entry({ queueName: 'KF-NETPRT009', deviceError: { code: 'CONNECTION_TIMEOUT', message: 'no answer' } }) },
    ]);
    expect(merged[0].deviceError?.code).toBe('CONNECTION_TIMEOUT');
  });

  it('keeps carried-over device data but does not count it as answered', () => {
    const merged = mergePrinters([
      {
        server: 'PRSRV',
        entry: withDevice(
          {
            queueName: 'KF-NETPRT010',
            location: 'Server-Label',
            deviceError: { code: 'CONNECTION_TIMEOUT', message: 'no answer' },
            deviceDataFromUtc: '2026-06-01T08:00:00Z',
          },
          { serialNumber: 'S10', model: 'UTAX', sysLocation: 'Floor 2' },
        ),
      },
    ]);

    expect(merged[0].serialNumber).toBe('S10');
    expect(merged[0].deviceDataFromUtc).toBe('2026-06-01T08:00:00Z');
    expect(merged[0].deviceAnswered).toBe(false);
    // Not "answered", so the print server label stays the displayed location and no mismatch is flagged
    expect(displayedLocation(merged[0])).toBe('Server-Label');
    expect(locationFlag(merged[0])).toBeNull();
  });

  it('prefers a queue with fresh data over one with carried-over data', () => {
    const merged = mergePrinters([
      { server: 'PRSRV', entry: withDevice({ queueName: 'KF-NETPRT011_B', deviceAddress: '10.0.0.11', deviceDataFromUtc: '2026-06-01T08:00:00Z' }, { serialNumber: 'S11', model: 'OLD' }) },
      { server: 'PRSRV', entry: withDevice({ queueName: 'KF-NETPRT011', deviceAddress: '10.0.0.11' }, { serialNumber: 'S11', model: 'NEW', status: 'Idle' }) },
    ]);

    expect(merged).toHaveLength(1);
    expect(merged[0].model).toBe('NEW');
    expect(merged[0].deviceAnswered).toBe(true);
    expect(merged[0].deviceDataFromUtc).toBeNull();
  });

  it('exposes the resolved device IP separately from the (possibly hostname) address', () => {
    const merged = mergePrinters([
      { server: 'PRSRV', entry: withDevice({ queueName: 'KF-1', deviceAddress: 'PK-NETPRT012', deviceIp: '172.20.20.12' }, { serialNumber: 'S1' }) },
    ]);
    expect(merged[0].deviceAddress).toBe('PK-NETPRT012');
    expect(merged[0].deviceIp).toBe('172.20.20.12');
  });
});

describe('displayedLocation / locationFlag', () => {
  const swapped = mergePrinters([
    { server: 'PRSRV', entry: withDevice({ queueName: 'A', location: 'EG Flur' }, { serialNumber: 'S1', sysLocation: 'Denkingen 1. OG' }) },
  ])[0];
  const deviceEmpty = mergePrinters([
    { server: 'PRSRV', entry: withDevice({ queueName: 'B', location: 'EG Flur' }, { serialNumber: 'S2', sysLocation: null }) },
  ])[0];
  const agreeing = mergePrinters([
    { server: 'PRSRV', entry: withDevice({ queueName: 'C', location: 'eg flur' }, { serialNumber: 'S3', sysLocation: 'EG Flur' }) },
  ])[0];
  const unreachable = mergePrinters([
    { server: 'PRSRV', entry: entry({ queueName: 'D', location: 'EG Flur', deviceError: { code: 'CONNECTION_TIMEOUT', message: 'x' } }) },
  ])[0];

  it('shows the device SNMP location as the truth and flags a mismatch', () => {
    expect(displayedLocation(swapped)).toBe('Denkingen 1. OG');
    expect(locationFlag(swapped)).toEqual({ reason: 'mismatch', serverLocation: 'EG Flur' });
  });
  it('flags a device without a location and surfaces the print server value', () => {
    expect(displayedLocation(deviceEmpty)).toBeNull();
    expect(locationFlag(deviceEmpty)).toEqual({ reason: 'missing', serverLocation: 'EG Flur' });
  });
  it('does not flag when device and print server agree (case-insensitive)', () => {
    expect(locationFlag(agreeing)).toBeNull();
  });
  it('never flags an unreachable device and falls back to the print server location', () => {
    expect(displayedLocation(unreachable)).toBe('EG Flur');
    expect(locationFlag(unreachable)).toBeNull();
  });
});

describe('filterPrinters / groupPrinters', () => {
  const merged = mergePrinters([
    { server: 'PRSRV', entry: withDevice({ queueName: 'KF-NETPRT001', location: 'EG' }, { serialNumber: 'VCF123', model: 'UTAX' }) },
    { server: 'PRSRV', entry: withDevice({ queueName: 'PK-NETPRT002' }, { serialNumber: 'ZZ9', model: 'Kyocera' }) },
    { server: 'OTHERSRV', entry: entry({ queueName: 'PK-NETPRT004', deviceError: { code: 'CONNECTION_TIMEOUT', message: 'no answer' } }) },
  ]);
  it('filters across name, serial, model and location', () => {
    expect(filterPrinters(merged, 'vcf').map((p) => p.name)).toEqual(['KF-NETPRT001']);
    expect(filterPrinters(merged, 'kyocera').map((p) => p.name)).toEqual(['PK-NETPRT002']);
    expect(filterPrinters(merged, 'eg').map((p) => p.name)).toEqual(['KF-NETPRT001']);
  });
  it('groups by site code', () => {
    expect(groupPrinters(merged, 'site').map((g) => g.label)).toEqual(['KF', 'PK']);
  });
  it('groups by model, server and status with honest unknown labels', () => {
    expect(groupPrinters(merged, 'model').map((g) => g.label)).toEqual(['Kyocera', 'Unknown model', 'UTAX']);
    expect(groupPrinters(merged, 'server').map((g) => g.label)).toEqual(['OTHERSRV', 'PRSRV']);
    expect(groupPrinters(merged, 'status').map((g) => g.label)).toEqual(['Unknown']);
  });
  it('groups provider states by their canonical semantic status family', () => {
    const printers = mergePrinters([
      { server: 'PRSRV', entry: withDevice({ queueName: 'KF-NETPRT011' }, { status: 'Idle' }) },
      { server: 'PRSRV', entry: withDevice({ queueName: 'KF-NETPRT012' }, { status: 'Printing' }) },
      { server: 'PRSRV', entry: withDevice({ queueName: 'KF-NETPRT013' }, { status: 'Warmup' }) },
      { server: 'PRSRV', entry: withDevice({ queueName: 'KF-NETPRT014' }, { status: 'Other' }) },
      { server: 'PRSRV', entry: entry({ queueName: 'KF-NETPRT015', deviceError: { code: 'CONNECTION_TIMEOUT', message: 'no answer' } }) },
    ]);

    expect(groupPrinters(printers, 'status').map((group) => ({
      label: group.label,
      count: group.printers.length,
    }))).toEqual([
      { label: 'Idle', count: 1 },
      { label: 'Running', count: 2 },
      { label: 'Unknown', count: 2 },
    ]);
  });
  it("'none' keeps a single flat group", () => {
    const flat = groupPrinters(merged, 'none');
    expect(flat).toHaveLength(1);
    expect(flat[0].label).toBe('');
    expect(flat[0].printers).toHaveLength(3);
  });
});

describe('ipInCidr / classifySubnet', () => {
  it('matches IPv4 addresses inside a CIDR block', () => {
    expect(ipInCidr('172.20.20.12', '172.20.20.0/24')).toBe(true);
    expect(ipInCidr('172.20.21.12', '172.20.20.0/24')).toBe(false);
    expect(ipInCidr('192.168.20.5', '192.168.20.0/24')).toBe(true);
    expect(ipInCidr('10.0.0.1', '10.0.0.0/8')).toBe(true);
    expect(ipInCidr('not-an-ip', '172.20.20.0/24')).toBe(false);
  });

  const policy: NetworkPolicyResult = {
    printerSubnets: ['172.20.20.0/24', '172.21.18.0/24', '172.21.20.0/24'],
    legacySubnets: ['192.168.20.0/24'],
    dhcpServer: null,
  };

  it('classifies a device by its subnet placement across both sites', () => {
    expect(classifySubnet('172.20.20.12', policy)).toBe('target');
    expect(classifySubnet('172.21.18.5', policy)).toBe('target'); // site 2 WLAN printers
    expect(classifySubnet('172.21.20.9', policy)).toBe('target'); // site 2 LAN printers
    expect(classifySubnet('172.21.5.1', policy)).toBe('foreign'); // other VLAN at site 2
    expect(classifySubnet('192.168.20.7', policy)).toBe('legacy');
    expect(classifySubnet('10.9.9.9', policy)).toBe('foreign');
    expect(classifySubnet(null, policy)).toBe('unknown');
  });
});

describe('lowestTonerPercent', () => {
  it('returns the minimum reported level or null', () => {
    expect(lowestTonerPercent([{ description: 'B', percent: 40, isLow: false }, { description: 'C', percent: 8, isLow: true }])).toBe(8);
    expect(lowestTonerPercent([{ description: 'B', percent: null, isLow: false }])).toBeNull();
  });
});
