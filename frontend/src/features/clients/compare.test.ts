import { describe, expect, it } from 'vitest';
import type { HardwareSnapshot, SecurityFinding } from '../../shared/api-types';
import { compareFindings, compareInventory, compareSoftware, setDiff } from './compare';

function finding(over: Partial<SecurityFinding>): SecurityFinding {
  return {
    findingId: 'WEC-SEC-X',
    title: 'x',
    description: 'x',
    severity: 'LOW',
    category: 'SYSTEM',
    affectedResource: 'x',
    recommendation: 'x',
    evidence: {},
    requiredPrivilege: null,
    ...over,
  } as SecurityFinding;
}

function snapshot(over: Partial<HardwareSnapshot>): HardwareSnapshot {
  return {
    cpu: { name: 'Intel i7', physicalCores: 4, logicalProcessors: 8, maxClockSpeedMhz: 3000 },
    memoryBanks: [{ manufacturer: null, partNumber: null, capacityBytes: 8 * 1024 ** 3, speedMtps: null }],
    disks: [{ model: 'SSD', sizeBytes: 256 * 1024 ** 3, interfaceType: null, mediaType: null }],
    operatingSystem: { caption: 'Windows 11 Pro', version: '10.0.22631', buildNumber: '22631', architecture: '64-bit' },
    installedSoftware: [{ name: 'Firefox', version: '1', publisher: null }],
    networkAdapters: null,
    gpus: null,
    monitors: null,
    installedSoftwareError: null,
    userEvidence: null,
    ...over,
  };
}

describe('setDiff', () => {
  it('splits into onlyA / onlyB / both, case-insensitively and de-duplicated', () => {
    expect(setDiff(['Firefox', 'Chrome', 'chrome'], ['CHROME', 'Edge'])).toEqual({
      onlyA: ['Firefox'],
      onlyB: ['Edge'],
      both: ['Chrome'],
    });
  });
});

describe('compareInventory', () => {
  it('flags identical vs differing properties', () => {
    const a = snapshot({});
    const b = snapshot({
      cpu: { name: 'AMD Ryzen', physicalCores: 4, logicalProcessors: 8, maxClockSpeedMhz: 3000 },
      memoryBanks: [
        { manufacturer: null, partNumber: null, capacityBytes: 8 * 1024 ** 3, speedMtps: null },
        { manufacturer: null, partNumber: null, capacityBytes: 8 * 1024 ** 3, speedMtps: null },
      ],
    });
    const rows = compareInventory(a, b);
    const os = rows.find((r) => r.label === 'Operating system');
    const cpu = rows.find((r) => r.label === 'CPU');
    const mem = rows.find((r) => r.label === 'Memory');
    expect(os?.same).toBe(true);
    expect(cpu).toMatchObject({ a: 'Intel i7', b: 'AMD Ryzen', same: false, match: 'different' });
    expect(mem).toMatchObject({ a: '8.0 GB', b: '16.0 GB', same: false, match: 'different' });
  });

  it('normalizes GPU whitespace and order before deciding equality', () => {
    const a = snapshot({ gpus: [
      { name: ' NVIDIA RTX  ', memoryBytes: null, driverVersion: null },
      { name: 'Intel  UHD', memoryBytes: null, driverVersion: null },
    ] });
    const b = snapshot({ gpus: [
      { name: 'Intel UHD', memoryBytes: null, driverVersion: null },
      { name: 'NVIDIA RTX', memoryBytes: null, driverVersion: null },
    ] });

    expect(compareInventory(a, b).find((row) => row.label === 'GPU')).toMatchObject({
      a: 'Intel UHD, NVIDIA RTX',
      b: 'Intel UHD, NVIDIA RTX',
      match: 'equivalent',
      rule: 'Compared after trimming whitespace and ignoring device order.',
    });
  });

  it('uses documented byte tolerances while retaining the reported values', () => {
    const a = snapshot({
      memoryBanks: [{ manufacturer: null, partNumber: null, capacityBytes: 8 * 1024 ** 3, speedMtps: null }],
      disks: [{ model: 'SSD', sizeBytes: 500 * 1024 ** 3, interfaceType: null, mediaType: null }],
    });
    const b = snapshot({
      memoryBanks: [{ manufacturer: null, partNumber: null, capacityBytes: 8 * 1024 ** 3 + 32 * 1024 ** 2, speedMtps: null }],
      disks: [{ model: 'SSD', sizeBytes: 504 * 1024 ** 3, interfaceType: null, mediaType: null }],
    });
    const rows = compareInventory(a, b);

    expect(rows.find((row) => row.label === 'Memory')).toMatchObject({ match: 'equivalent', same: true });
    expect(rows.find((row) => row.label === 'Storage')).toMatchObject({ match: 'equivalent', same: true });
  });
});

describe('compareSoftware', () => {
  it('does not infer product absence from an unavailable software capture', () => {
    const a = snapshot({ installedSoftware: [{ name: 'Firefox', version: '1', publisher: null }] });
    const b = snapshot({
      installedSoftware: null,
      installedSoftwareError: { code: 'ACCESS_DENIED', message: 'Registry access denied.' },
    });

    expect(compareSoftware(a, b)).toEqual({
      status: 'unknown',
      unavailableSides: ['b'],
      onlyA: [],
      onlyB: [],
      both: [],
      versionDifferences: [],
    });
  });

  it('separates product presence from reported version differences', () => {
    const a = snapshot({ installedSoftware: [
      { name: 'Firefox', version: '125', publisher: null },
      { name: '7-Zip', version: '24.0', publisher: null },
    ] });
    const b = snapshot({ installedSoftware: [
      { name: 'firefox', version: '126', publisher: null },
      { name: 'Edge', version: '1', publisher: null },
    ] });
    const result = compareSoftware(a, b);

    expect(result).toMatchObject({
      status: 'available',
      onlyA: ['7-Zip'],
      onlyB: ['Edge'],
      both: ['Firefox'],
    });
    expect(result.versionDifferences).toEqual([expect.objectContaining({
      label: 'Firefox', a: '125', b: '126', match: 'different',
    })]);
  });
});

describe('compareFindings', () => {
  it('diffs problems by title and excludes coverage notes', () => {
    const a = [
      finding({ findingId: 'A', title: 'SMBv1 enabled' }),
      finding({ findingId: 'X-LOCAL-ONLY', title: 'coverage' }),
    ];
    const b = [finding({ findingId: 'B', title: 'Firewall off' })];
    expect(compareFindings(a, b)).toEqual({
      onlyA: ['SMBv1 enabled'],
      onlyB: ['Firewall off'],
      both: [],
    });
  });
});
