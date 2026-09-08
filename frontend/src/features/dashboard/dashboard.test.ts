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
  deriveVulnerabilityTile,
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
    expect(deriveInventoryTile([], 86_400, new Date('2026-07-03T09:00:00Z'))).toMatchObject({
      value: 'No data',
      state: 'missing',
      coverage: '0 hosts with stored inventory',
    });

    const hosts: StoredInventoryHost[] = [
      { host: 'PC1', capturedAtUtc: '2026-07-02T08:00:00Z' },
      { host: 'PC2', capturedAtUtc: '2026-07-03T08:00:00Z' },
    ];
    const tile = deriveInventoryTile(hosts, 86_400, new Date('2026-07-03T09:00:00Z'));
    expect(tile.value).toBe('2 stored hosts');
    expect(tile.state).toBe('partial');
    expect(tile.capturedAtUtc).toBe('2026-07-03T08:00:00Z');
    expect(tile.coverage).toBe('1 of 2 hosts within 24h freshness window');
    expect(tile.note).toContain(new Date('2026-07-03T08:00:00Z').toLocaleString());
  });

  it('inventory: never presents stored hosts as the entire fleet', () => {
    const hosts = Array.from({ length: 41 }, (_, index): StoredInventoryHost => ({
      host: `PC-${index + 1}`,
      capturedAtUtc: '2026-07-03T08:00:00Z',
    }));

    const tile = deriveInventoryTile(hosts, 86_400, new Date('2026-07-03T09:00:00Z'));

    expect(tile.value).toBe('41 stored hosts');
    expect(tile.source).toBe('Stored WMI/CIM inventory snapshots');
    expect(tile.coverage).toBe('41 of 41 hosts within 24h freshness window');
  });

  it('security: critical findings drive the danger tone', () => {
    expect(deriveSecurityTile(null, 86_400, new Date('2026-07-03T09:00:00Z'))).toMatchObject({
      value: 'No data',
      state: 'missing',
      coverage: 'No security checks evaluated',
    });

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
    const tile = deriveSecurityTile(scan, 86_400, new Date('2026-07-03T09:00:00Z'));
    expect(tile.value).toBe('2 critical/high');
    expect(tile.tone).toBe('danger');
    expect(tile.state).toBe('fresh');
    expect(tile.coverage).toBe('13 of 13 applicable checks evaluated');
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
    }, 86_400, new Date('2026-07-03T09:00:00Z'));
    expect(tile.value).toBe('1 findings');
    expect(tile.tone).toBe('success');
    expect(tile.state).toBe('fresh');
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
    }, 86_400, new Date('2026-07-03T09:00:00Z'));

    expect(tile.value).toBe('0 findings');
    expect(tile.tone).toBe('warning');
    expect(tile.state).toBe('partial');
    expect(tile.note).toContain('coverage incomplete');
  });

  it('security: stale clean scan is not presented as healthy', () => {
    const tile = deriveSecurityTile({
      scanId: 1,
      host: 'PC1',
      startedAtUtc: '2026-07-01T08:00:00Z',
      completedAtUtc: '2026-07-01T08:01:00Z',
      status: 'COMPLETED',
      findings: [],
      checkResults: [],
      coverageVersion: 1,
      coverage: completeCoverage,
    }, 86_400, new Date('2026-07-03T09:00:00Z'));

    expect(tile).toMatchObject({ value: '0 findings', state: 'stale', tone: 'warning' });
    expect(tile.note).toContain('older than the 24h freshness window');
  });

  it('vulnerabilities: sync and failed sources never expose a healthy zero', () => {
    const overview = {
      sync: {
        phase: 'IMPORTING_CURRENT_RUNS',
        running: true,
        startedAtUtc: '2026-07-03T08:00:00Z',
        lastSuccessfulSyncUtc: null,
        error: null,
        completedScans: 2,
        totalScans: 5,
        historySupported: true,
        serverVersion: '10.8',
      },
      includedScans: 0,
      excludedScans: 0,
      assets: 0,
      matchedAssets: 0,
      unmatchedAssets: 0,
      criticalAssets: 0,
      highAssets: 0,
      criticalInstances: 0,
      highInstances: 0,
      staleScans: 0,
    } as const;

    expect(deriveVulnerabilityTile(overview, null)).toMatchObject({
      value: 'Syncing',
      state: 'loading',
      coverage: '2 of 5 scans imported',
    });

    expect(deriveVulnerabilityTile({
      ...overview,
      sync: { ...overview.sync, phase: 'FAILED', running: false, error: 'Nessus unavailable' },
    }, null)).toMatchObject({ value: 'Unavailable', state: 'error', tone: 'danger' });
  });

  it('vulnerabilities: zero is healthy only with a successful, covered source', () => {
    const overview = {
      sync: {
        phase: 'COMPLETED' as const,
        running: false,
        startedAtUtc: '2026-07-03T08:00:00Z',
        lastSuccessfulSyncUtc: '2026-07-03T08:05:00Z',
        error: null,
        completedScans: 5,
        totalScans: 5,
        historySupported: true,
        serverVersion: '10.8',
      },
      includedScans: 5,
      excludedScans: 0,
      assets: 20,
      matchedAssets: 20,
      unmatchedAssets: 0,
      criticalAssets: 0,
      highAssets: 0,
      criticalInstances: 0,
      highInstances: 0,
      staleScans: 0,
    };
    const trend = {
      verdict: 'STABLE' as const,
      points: [],
      commonAssets: 20,
      newAssets: 0,
      removedAssets: 0,
      comparison: {
        startDayUtc: '2026-07-01', endDayUtc: '2026-07-03',
        startCritical: 0, endCritical: 0, startHigh: 0, endHigh: 0,
        startMedium: 0, endMedium: 0, startLow: 0, endLow: 0,
        decidingSeverity: null,
      },
    };

    expect(deriveVulnerabilityTile(overview, trend)).toMatchObject({
      value: '0',
      state: 'fresh',
      tone: 'success',
      capturedAtUtc: '2026-07-03T08:05:00Z',
      coverage: '5 included scans · 0 stale · trend compares 20 common assets (2026-07-01 → 2026-07-03)',
    });
    expect(deriveVulnerabilityTile({ ...overview, staleScans: 5 }, trend)).toMatchObject({
      value: '0',
      state: 'stale',
      tone: 'warning',
    });
  });

  it('print: counts toner-low across snapshots and warns', () => {
    const servers: StoredPrintServer[] = [
      { server: 'PRSRV1', capturedAtUtc: '2026-07-03T08:00:00Z', snapshotCount: 1 },
    ];
    const snapshots: PrintServerSnapshot[] = [
      {
        server: 'PRSRV1',
        capturedAtUtc: '2026-07-03T08:00:00Z',
        unusedPorts: [],
        unusedDrivers: [],
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
            deviceDataFromUtc: null,
          },
        ],
      },
    ];
    const tile = derivePrintTile(servers, snapshots);
    expect(tile.value).toBe('1 server');
    expect(tile.tone).toBe('warning');
    expect(tile.note).toContain('1 printer(s) low on toner');

    expect(derivePrintTile([], [])).toMatchObject({ value: 'No data', state: 'missing' });
  });

  it('patch: reflects the opsi connection state', () => {
    expect(derivePatchTile(null)).toMatchObject({ value: 'Unavailable', state: 'error' });
    expect(
      derivePatchTile({
        connected: true,
        serverUrl: 'https://opsi.local:4447/',
        userName: 'admin',
        opsiVersion: '4.3',
        defaultDepotFilter: 'Denkingen',
        connectionError: null,
      }),
    ).toMatchObject({ value: 'Connected', tone: 'success' });
  });
});
