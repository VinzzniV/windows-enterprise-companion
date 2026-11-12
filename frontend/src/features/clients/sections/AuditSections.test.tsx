import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { DiagnosticRunResult, HardwareInfoResult, SecurityScanResult } from '../../../shared/api-types';
import { HealthSection } from './HealthSection';
import { InventorySection } from './InventorySection';
import { SecuritySection } from './SecuritySection';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../../shared/bridge/bridgeClient', () => ({
  invoke: invokeMock,
  BridgeInvokeError: class extends Error {},
  BridgeTimeoutError: class extends Error {},
  BridgeUnavailableError: class extends Error {},
  BridgeCancelledError: class extends Error {},
}));

const inventory: HardwareInfoResult = {
  host: 'TEST-PC',
  capturedAtUtc: '2026-08-19T08:00:00Z',
  fromCache: true,
  snapshot: {
    cpu: { name: 'Visible CPU', physicalCores: 4, logicalProcessors: 8, maxClockSpeedMhz: 4000 },
    memoryBanks: [
      { manufacturer: null, partNumber: null, capacityBytes: 16 * 1024 ** 3, speedMtps: null },
      { manufacturer: null, partNumber: null, capacityBytes: 16 * 1024 ** 3, speedMtps: null },
    ],
    disks: [{ model: 'SSD', sizeBytes: 1024 ** 4, interfaceType: 'NVMe', mediaType: 'SSD' }],
    operatingSystem: { caption: 'Windows 11', version: '10.0', buildNumber: '26200', architecture: '64-bit' },
    networkAdapters: [],
    gpus: [],
    monitors: [],
    installedSoftware: [],
    installedSoftwareError: null,
    userEvidence: null,
  },
};

const scan: SecurityScanResult = {
  scanId: 42,
  host: 'TEST-PC',
  startedAtUtc: '2026-08-19T08:00:00Z',
  completedAtUtc: '2026-08-19T08:00:05Z',
  status: 'COMPLETED',
  findings: [
    {
      findingId: 'F-HIGH',
      title: 'Firewall disabled',
      description: 'Description',
      severity: 'HIGH',
      category: 'FIREWALL',
      affectedResource: 'Firewall',
      evidence: {},
      recommendation: 'Enable it.',
      requiredPrivilege: null,
      capturedAtUtc: '2026-08-19T08:00:05Z',
    },
  ],
  checkResults: [{ checkId: 'FIREWALL', status: 'SUCCEEDED', findings: [], failure: null }],
  coverageVersion: 1,
  coverage: {
    isKnown: true,
    totalChecks: 1,
    succeededChecks: 1,
    failedChecks: 0,
    requiresElevationChecks: 0,
    notApplicableChecks: 0,
    applicableChecks: 1,
    isComplete: true,
  },
};

const healthRun: DiagnosticRunResult = {
  startedAtUtc: '2026-08-19T08:00:00Z',
  completedAtUtc: '2026-08-19T08:00:05Z',
  results: [{
    diagnosticId: 'WEC-DIAG-SYS-DISKSPACE',
    title: 'Stored disk result',
    status: 'PASS',
    category: 'SYSTEM',
    affectedResource: 'Fixed drives',
    evidence: { free: '100 GB' },
    suggestedNextSteps: [],
    requiredPrivilege: null,
    capturedAtUtc: '2026-08-19T08:00:05Z',
  }],
};

describe('client audit sections', () => {
  beforeEach(() => {
    invokeMock.mockReset();
  });

  it('keeps the routed inventory snapshot visible during a failed refresh', async () => {
    let rejectRefresh!: (reason: Error) => void;
    const pendingRefresh = new Promise<HardwareInfoResult>((_resolve, reject) => {
      rejectRefresh = reject;
    });
    let inventoryCalls = 0;
    invokeMock.mockImplementation((_module: string, action: string) => {
      if (action === 'getHardwareInfo') {
        inventoryCalls += 1;
        return inventoryCalls === 1 ? Promise.resolve(inventory) : pendingRefresh;
      }
      if (action === 'getDiskEncryptionStatus') return Promise.resolve({ volumes: [] });
      return Promise.reject(new Error(`Unexpected action: ${action}`));
    });

    render(<InventorySection target={{ host: 'TEST-PC' }} />);

    expect(await screen.findByText('Visible CPU')).toBeDefined();
    expect(screen.getByLabelText('Inventory totals').textContent).toContain('32.0 GBMemory');
    await userEvent.click(screen.getByRole('button', { name: 'Refresh' }));
    expect(screen.getByText('Refreshing — previous snapshot remains visible')).toBeDefined();
    expect(screen.getByText('Visible CPU')).toBeDefined();

    rejectRefresh(new Error('provider unavailable'));
    expect(await screen.findByText(/Refresh failed; the previous snapshot is still shown/)).toBeDefined();
    expect(screen.getByText('Visible CPU')).toBeDefined();
  });

  it('shows coverage-aware history and accessible severity filters in the routed security section', async () => {
    invokeMock.mockImplementation((_module: string, action: string) => {
      if (action === 'getLatestScan') return Promise.resolve({ scan });
      if (action === 'getScanHistory') {
        return Promise.resolve({
          scans: [{
            scanId: scan.scanId,
            startedAtUtc: scan.startedAtUtc,
            completedAtUtc: scan.completedAtUtc,
            status: scan.status,
            findingCount: 1,
            severityCounts: [{ severity: 'HIGH', count: 1 }],
            coverage: scan.coverage,
          }],
          changesSinceLastScan: null,
        });
      }
      return Promise.reject(new Error(`Unexpected action: ${action}`));
    });

    render(<SecuritySection target={{ host: 'TEST-PC' }} />);

    expect(await screen.findByText('Coverage complete')).toBeDefined();
    expect(screen.getByRole('group', { name: 'Filter findings by severity' })).toBeDefined();
    expect(screen.getByRole('button', { name: 'HIGH: 1 finding' }).textContent).toBe('HIGH (1)');
    expect(await screen.findByText('Scan history (1)')).toBeDefined();
  });

  it('keeps a saved security scan visible when a new scan fails', async () => {
    invokeMock.mockImplementation((_module: string, action: string) => {
      if (action === 'getLatestScan') return Promise.resolve({ scan });
      if (action === 'getScanHistory') return Promise.resolve({ scans: [], changesSinceLastScan: null });
      if (action === 'runScan') return Promise.reject(new Error('provider unavailable'));
      return Promise.reject(new Error(`Unexpected action: ${action}`));
    });

    render(<SecuritySection target={{ host: 'TEST-PC' }} />);
    await userEvent.click(await screen.findByRole('button', { name: 'Re-run scan' }));

    expect(await screen.findByText('Scan failed; the previous saved scan is still shown.')).toBeDefined();
    expect(screen.getByText('Firewall disabled')).toBeDefined();
  });

  it('keeps a saved health result visible when a new check fails', async () => {
    invokeMock.mockImplementation((_module: string, action: string) => {
      if (action === 'getLatestDiagnostics') return Promise.resolve({ run: healthRun });
      if (action === 'runDiagnostics') return Promise.reject(new Error('provider unavailable'));
      return Promise.reject(new Error(`Unexpected action: ${action}`));
    });

    render(<HealthSection target={{ host: 'TEST-PC' }} />);
    await userEvent.click(await screen.findByRole('button', { name: 'Re-run health check' }));

    expect(await screen.findByText('Health check failed; the previous saved result is still shown.')).toBeDefined();
    expect(screen.getByText('Stored disk result')).toBeDefined();
  });
});
