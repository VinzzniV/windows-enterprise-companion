import { describe, expect, it } from 'vitest';
import type { PrinterEntry } from '../../shared/api-types';
import {
  baseQueueName,
  filterPrinters,
  groupPrinters,
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
    location: null,
    comment: null,
    device: null,
    deviceError: null,
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
    expect(groupPrinters(merged, 'status').map((g) => g.label)).toEqual(['Not answering', 'Unknown']);
  });
  it("'none' keeps a single flat group", () => {
    const flat = groupPrinters(merged, 'none');
    expect(flat).toHaveLength(1);
    expect(flat[0].label).toBe('');
    expect(flat[0].printers).toHaveLength(3);
  });
});

describe('lowestTonerPercent', () => {
  it('returns the minimum reported level or null', () => {
    expect(lowestTonerPercent([{ description: 'B', percent: 40, isLow: false }, { description: 'C', percent: 8, isLow: true }])).toBe(8);
    expect(lowestTonerPercent([{ description: 'B', percent: null, isLow: false }])).toBeNull();
  });
});
