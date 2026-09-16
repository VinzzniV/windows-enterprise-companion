import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ClientOverviewResult, ItHygieneResult } from '../../../shared/api-types';
import { EnvironmentProvider } from '../../../shared/environment/EnvironmentContext';
import { TargetProvider } from '../../../shared/targets/TargetContext';
import { OverviewSection } from './OverviewSection';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../../shared/bridge/bridgeClient', () => ({
  invoke: invokeMock,
  invokeCancellable: (module: string, action: string, payload: unknown) => ({ requestId: 'request-id', promise: invokeMock(module, action, payload), cancel: vi.fn() }),
  subscribe: vi.fn(() => () => {}),
  BridgeInvokeError: class extends Error {},
  BridgeCancelledError: class extends Error {},
  BridgeTimeoutError: class extends Error {},
  BridgeUnavailableError: class extends Error {},
}));

const environmentResult: ItHygieneResult = {
  assessedAtUtc: '2026-08-21T07:00:00Z',
  domainName: 'corp.local',
  sources: {
    activeDirectory: { availability: 'AVAILABLE', error: null },
    kaspersky: { availability: 'AVAILABLE', error: null },
    opsi: { availability: 'AVAILABLE', error: null },
    nessus: { availability: 'AVAILABLE', error: null },
  },
  summary: { total: 1, adComputers: 1, kasperskyComputers: 1, opsiComputers: 1, nessusComputers: 1, healthy: 0, problems: 1, incomplete: 0, stale: 1, missingKaspersky: 0, orphanKaspersky: 0, missingOpsi: 0, orphanOpsi: 0, outdated: 0, missingNessus: 0, staleNessus: 0, nessusCritical: 0, nessusHigh: 0 },
  devices: [{
    computerName: 'PC-42', hostName: 'PC-42.corp.local',
    evidenceKey: null, correlation: 'ALIAS_CANDIDATE', correlationExplanation: 'Name candidate only.', canTargetWindows: true,
    activeDirectory: { exists: true, enabled: true, dnsHostName: 'PC-42.corp.local', operatingSystem: 'Windows 11', description: null, distinguishedName: null, organizationalUnit: null, lastLogonDate: '2026-08-20T07:00:00Z' },
    kaspersky: { exists: true, lastSeen: '2026-08-20T07:00:00Z', agentVersion: '16.0', kesVersion: '21.25', administrationGroup: 'Clients' },
    opsi: { exists: true, clientId: 'pc-42.corp.local', description: null, depotId: 'depot-1', lastSeen: '2026-05-01T07:00:00Z', clientAgentVersion: '4.3' },
    nessus: { exists: true, assetId: 'asset-1', ipAddress: '10.0.0.42', lastCompletedScanUtc: '2026-08-20T07:00:00Z', critical: 0, high: 0, medium: 0, low: 0, info: 0, ports: [], scanSources: ['Clients'] },
    assessment: { status: 'WARNING', findings: [{ code: 'STALE_OPSI', severity: 'WARNING', message: 'opsi last seen is stale.' }] },
  }],
};

const storedOverview: ClientOverviewResult = {
  host: 'PC-42.corp.local',
  inventory: {
    metadata: { source: 'Inventory', provenance: 'Persisted WMI/CIM hardware snapshot', freshness: 'FRESH', capturedAtUtc: '2026-08-21T06:00:00Z', ageSeconds: 3600, isComplete: true, coverage: 'Hardware and operating-system snapshot available.', detailSection: 'inventory' },
    cpuName: 'Intel Core', physicalCores: 8, logicalProcessors: 16, totalMemoryBytes: 17179869184,
    operatingSystem: 'Windows 11 Enterprise', operatingSystemVersion: '10.0', operatingSystemBuild: '26100', architecture: '64-bit',
    disks: [{ model: 'NVMe', sizeBytes: 512000000000, interfaceType: 'NVMe' }],
  },
  software: {
    metadata: { source: 'Installed software', provenance: 'Persisted Inventory software capture', freshness: 'FRESH', capturedAtUtc: '2026-08-21T06:00:00Z', ageSeconds: 3600, isComplete: true, coverage: 'Complete capture with 12 installed entries.', detailSection: 'inventory' },
    installedCount: 12,
    sample: [{ name: 'Microsoft 365 Apps', version: '16.0', publisher: 'Microsoft' }],
  },
  health: {
    metadata: { source: 'Health', provenance: 'Latest persisted on-demand Health run', freshness: 'STALE', capturedAtUtc: '2026-08-18T07:00:00Z', ageSeconds: 262800, isComplete: true, coverage: '4 of 4 expected checks observed.', detailSection: 'diagnostics' },
    criticalCount: 0, warningCount: 1, unknownCount: 0, healthyCount: 3,
    issues: [{ diagnosticId: 'SERVICES', title: 'Service stopped', status: 'Warning', affectedResource: 'Spooler' }],
  },
  security: {
    metadata: { source: 'Security', provenance: 'Latest persisted Security scan and per-check coverage', freshness: 'FRESH', capturedAtUtc: '2026-08-21T06:30:00Z', ageSeconds: 1800, isComplete: true, coverage: '13 of 13 applicable checks succeeded.', detailSection: 'security' },
    scanStatus: 'Completed', criticalCount: 1, highCount: 0, mediumCount: 1, lowCount: 0,
    topFindings: [{ findingId: 'BITLOCKER', title: 'BitLocker protection disabled', severity: 'Critical', affectedResource: 'C:' }],
  },
  users: {
    metadata: { source: 'Linked users', provenance: 'Latest stored WEC Inventory user evidence', freshness: 'FRESH', capturedAtUtc: '2026-08-21T06:00:00Z', ageSeconds: 3600, isComplete: true, coverage: 'Stored Inventory exposed one named observation and two unresolved local profiles.', detailSection: 'inventory' },
    unresolvedProfileCount: 2,
    observations: [{ directorySid: 'S-1-5-21-1-2-3-1104', accountDisplay: 'CORP\\alex', relationshipType: 'LAST_INTERACTIVE_USER', source: 'WEC Inventory', observedAtUtc: '2026-08-21T06:00:00Z', confidence: 'HIGH', explanation: 'Inventory observed this directory SID as the interactive user; this is not an ownership claim.' }],
  },
  sources: [
    { source: 'Inventory', provenance: 'Persisted WMI/CIM hardware snapshot', freshness: 'FRESH', capturedAtUtc: '2026-08-21T06:00:00Z', ageSeconds: 3600, isComplete: true, coverage: 'Hardware and operating-system snapshot available.', detailSection: 'inventory' },
    { source: 'Installed software', provenance: 'Persisted Inventory software capture', freshness: 'FRESH', capturedAtUtc: '2026-08-21T06:00:00Z', ageSeconds: 3600, isComplete: true, coverage: 'Complete capture with 12 installed entries.', detailSection: 'inventory' },
    { source: 'Health', provenance: 'Latest persisted on-demand Health run', freshness: 'STALE', capturedAtUtc: '2026-08-18T07:00:00Z', ageSeconds: 262800, isComplete: true, coverage: '4 of 4 expected checks observed.', detailSection: 'diagnostics' },
    { source: 'Security', provenance: 'Latest persisted Security scan and per-check coverage', freshness: 'FRESH', capturedAtUtc: '2026-08-21T06:30:00Z', ageSeconds: 1800, isComplete: true, coverage: '13 of 13 applicable checks succeeded.', detailSection: 'security' },
    { source: 'Linked users', provenance: 'Latest stored WEC Inventory user evidence', freshness: 'FRESH', capturedAtUtc: '2026-08-21T06:00:00Z', ageSeconds: 3600, isComplete: true, coverage: 'Stored Inventory exposed one named observation and two unresolved local profiles.', detailSection: 'inventory' },
  ],
};

function renderOverview() {
  return render(<MemoryRouter><TargetProvider><EnvironmentProvider><OverviewSection host="PC-42.corp.local" /></EnvironmentProvider></TargetProvider></MemoryRouter>);
}

describe('OverviewSection', () => {
  beforeEach(() => {
    invokeMock.mockReset();
    invokeMock.mockImplementation((module: string, action: string) => {
      if (module === 'targets' && action === 'list') return Promise.resolve({ targets: [] });
      if (module === 'clients' && action === 'getOverview') return Promise.resolve(storedOverview);
      if (module === 'employeelifecycle' && action === 'getHygiene') return Promise.resolve(environmentResult);
      if (module === 'connectivity' && action === 'probeHosts') return Promise.resolve({ results: [{ host: 'PC-42.corp.local', reachable: true, manageable: true }] });
      return Promise.reject(new Error(`Unexpected action ${module}/${action}`));
    });
  });

  it('shows stored inventory and Health evidence without loading management systems', async () => {
    renderOverview();

    expect(await screen.findByText('Windows 11 Enterprise')).toBeDefined();
    expect(screen.getByText('Service stopped')).toBeDefined();
    expect(screen.getAllByText('Stale').length).toBeGreaterThan(0);
    expect(screen.getByRole('link', { name: 'Open Health' }).getAttribute('href'))
      .toBe('/clients/PC-42.corp.local?section=diagnostics');
    expect(invokeMock.mock.calls.some((call) => call[0] === 'employeelifecycle')).toBe(false);
    expect(invokeMock.mock.calls.some((call) => call[0] === 'connectivity')).toBe(false);
  });

  it('loads management sources only after the explicit action', async () => {
    renderOverview();

    fireEvent.click(await screen.findByRole('button', { name: 'Load management sources' }));
    await screen.findByText('Client ID');

    const opsiCard = screen.getByText('Client ID').closest('[class*="rounded"]');
    expect(opsiCard).not.toBeNull();
    expect(opsiCard!.textContent).toContain('Stale');
    await waitFor(() => expect(invokeMock.mock.calls.some((call) => call[0] === 'employeelifecycle')).toBe(true));
    expect(invokeMock.mock.calls.some((call) => call[0] === 'connectivity')).toBe(false);
  });

  it('keeps missing sources explicit instead of presenting them as healthy', async () => {
    invokeMock.mockImplementation((module: string, action: string) => {
      if (module === 'targets' && action === 'list') return Promise.resolve({ targets: [] });
      if (module === 'clients' && action === 'getOverview') return Promise.resolve({
        ...storedOverview,
        software: null,
        security: null,
        sources: storedOverview.sources.map((source) => source.source === 'Installed software'
          ? { ...source, freshness: 'MISSING' as const, capturedAtUtc: null, ageSeconds: null, isComplete: false, coverage: 'No stored software capture.' }
          : source.source === 'Security'
            ? { ...source, freshness: 'MISSING' as const, capturedAtUtc: null, ageSeconds: null, isComplete: false, coverage: 'No stored Security scan.' }
            : source),
      });
      return Promise.reject(new Error(`Unexpected action ${module}/${action}`));
    });
    renderOverview();

    await screen.findByText('No stored software capture.');
    expect(screen.getByText('No stored Security scan.')).toBeDefined();
    expect(screen.getAllByText('Missing')).toHaveLength(4);
  });

  it('shows installed-software and Security context with reliable deep links', async () => {
    renderOverview();

    expect(await screen.findByText('12 installed applications')).toBeDefined();
    expect(screen.getByText('Microsoft 365 Apps')).toBeDefined();
    expect(screen.getByText('BitLocker protection disabled')).toBeDefined();
    expect(screen.getByRole('link', { name: 'Open software inventory' }).getAttribute('href'))
      .toBe('/clients/PC-42.corp.local?section=inventory');
    expect(screen.getByRole('link', { name: 'Open Security' }).getAttribute('href'))
      .toBe('/clients/PC-42.corp.local?section=security');
  });

  it('shows stored user evidence as an observation without claiming ownership', async () => {
    renderOverview();

    expect(await screen.findByText('CORP\\alex')).toBeDefined();
    expect(screen.getByText(/Last interactive user/)).toBeDefined();
    expect(screen.getByText(/not an ownership claim/)).toBeDefined();
    expect(screen.getByText(/2 additional local profiles/)).toBeDefined();
    expect(invokeMock.mock.calls.some((call) => call[0] === 'usermanagement')).toBe(false);
    expect(invokeMock.mock.calls.some((call) => call[0] === 'activedirectory')).toBe(false);
  });
});
