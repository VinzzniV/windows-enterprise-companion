import { describe, expect, it } from 'vitest';
import type {
  PrintServerSnapshot,
  SecurityFinding,
  SecurityScanResult,
  StoredInventoryHost,
  StoredPrintServer,
} from '../../shared/api-types';
import {
  deriveInventoryTile,
  derivePatchTile,
  derivePrintTile,
  deriveSecurityTile,
} from './dashboard';

function finding(overrides: Partial<SecurityFinding>): SecurityFinding {
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
    ...overrides,
  } as SecurityFinding;
}

const completeCoverage = {
  isKnown: true,
  totalChecks: 13,
  succeededChecks: 13,
  failedChecks: 0,
  requiresElevationChecks: 0,
  notApplicableChecks: 0,
  applicableChecks: 13,
  isComplete: true,
} as const;

describe('dashboard tile derivation', () => {
  it('inventory: empty vs populated with the newest capture', () => {
    expect(deriveInventoryTile([])).toMatchObject({ value: 'No hosts', tone: 'neutral' });

    const hosts: StoredInventoryHost[] = [
      { host: 'PC1', capturedAtUtc: '2026-07-01T08:00:00Z' },
      { host: 'PC2', capturedAtUtc: '2026-07-03T08:00:00Z' },
    ];
    const tile = deriveInventoryTile(hosts);
    expect(tile.value).toBe('2 hosts');
    expect(tile.note).toContain(new Date('2026-07-03T08:00:00Z').toLocaleString());
  });

  it('security: critical findings drive the danger tone', () => {
    expect(deriveSecurityTile(null)).toMatchObject({ value: 'No scan' });

    const scan: SecurityScanResult = {
      scanId: 1,
      host: 'PC1',
      startedAtUtc: '2026-07-03T08:00:00Z',
      completedAtUtc: '2026-07-03T08:01:00Z',
      status: 'COMPLETED',
      findings: [
        finding({ findingId: 'A', severity: 'CRITICAL' }),
        finding({ findingId: 'B', severity: 'HIGH' }),
      ],
      checkResults: [],
      coverageVersion: 1,
      coverage: completeCoverage,
    };
    const tile = deriveSecurityTile(scan);
    expect(tile.value).toBe('2 critical/high');
    expect(tile.tone).toBe('danger');
  });

  it('security: clean scan is success', () => {
    const tile = deriveSecurityTile({
      scanId: 1,
      host: 'PC1',
      startedAtUtc: '2026-07-03T08:00:00Z',
      completedAtUtc: '2026-07-03T08:01:00Z',
      status: 'COMPLETED',
      findings: [finding({ findingId: 'A', severity: 'LOW' })],
      checkResults: [],
      coverageVersion: 1,
      coverage: completeCoverage,
    });
    expect(tile.value).toBe('1 findings');
    expect(tile.tone).toBe('success');
  });

  it('security: incomplete coverage is never a success tile', () => {
    const tile = deriveSecurityTile({
      scanId: 1,
      host: 'PC1',
      startedAtUtc: '2026-07-03T08:00:00Z',
      completedAtUtc: '2026-07-03T08:01:00Z',
      status: 'COMPLETED_WITH_ERRORS',
      findings: [],
      checkResults: [],
      coverageVersion: 1,
      coverage: {
        ...completeCoverage,
        succeededChecks: 12,
        failedChecks: 1,
        isComplete: false,
      },
    });

    expect(tile.value).toBe('0 findings');
    expect(tile.tone).toBe('warning');
    expect(tile.note).toContain('coverage incomplete');
  });

  it('print: counts toner-low across snapshots and warns', () => {
    const servers: StoredPrintServer[] = [
      { server: 'PRSRV1', capturedAtUtc: '2026-07-03T08:00:00Z', snapshotCount: 1 },
    ];
    const snapshots: PrintServerSnapshot[] = [
      {
        server: 'PRSRV1',
        capturedAtUtc: '2026-07-03T08:00:00Z',
        printers: [
          {
            queueName: 'Q1',
            shareName: null,
            driverName: null,
            driverVersion: null,
            portName: null,
            deviceAddress: '10.0.0.1',
            deviceIp: '10.0.0.1',
            location: null,
            comment: null,
            device: {
              serialNumber: 'S1',
              model: null,
              sysName: null,
              sysLocation: null,
              status: null,
              pageCount: null,
              supplies: [{ description: 'Black', percent: 5, isLow: true }],
            },
            deviceError: null,
          },
        ],
      },
    ];
    const tile = derivePrintTile(servers, snapshots);
    expect(tile.value).toBe('1 server');
    expect(tile.tone).toBe('warning');
    expect(tile.note).toContain('1 printer(s) low on toner');

    expect(derivePrintTile([], [])).toMatchObject({ value: 'No servers' });
  });

  it('patch: reflects the opsi connection state', () => {
    expect(derivePatchTile(null)).toMatchObject({ value: 'Not connected' });
    expect(
      derivePatchTile({
        connected: true,
        serverUrl: 'https://opsi.local:4447/',
        userName: 'admin',
        opsiVersion: '4.3',
        defaultDepotFilter: 'Denkingen',
      }),
    ).toMatchObject({ value: 'Connected', tone: 'success' });
  });
});
