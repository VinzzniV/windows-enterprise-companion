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
    expect(cpu).toMatchObject({ a: 'Intel i7', b: 'AMD Ryzen', same: false });
    expect(mem).toMatchObject({ a: '8.0 GB', b: '16.0 GB', same: false });
  });
});

describe('compareSoftware', () => {
  it('diffs installed software by name, tolerating a null software list', () => {
    const a = snapshot({ installedSoftware: [{ name: 'Firefox', version: '1', publisher: null }] });
    const b = snapshot({ installedSoftware: null });
    expect(compareSoftware(a, b)).toEqual({ onlyA: ['Firefox'], onlyB: [], both: [] });
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
