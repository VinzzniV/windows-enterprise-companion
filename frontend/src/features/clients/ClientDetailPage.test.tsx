import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import type { ClientOverviewResult, HardwareInfoResult, ReportOverview, SecurityScanResult } from '../../shared/api-types';
import { TargetProvider } from '../../shared/targets/TargetContext';
import { EnvironmentProvider } from '../../shared/environment/EnvironmentContext';
import { ClientDetailPage } from './ClientDetailPage';
import { clientListScope } from './clientListNavigation';

const { invokeMock, BridgeInvokeErrorMock } = vi.hoisted(() => ({
  invokeMock: vi.fn(),
  BridgeInvokeErrorMock: class extends Error {
    constructor(readonly error: { code: string; message: string; details?: string | null }) {
      super(`${error.code}: ${error.message}`);
    }
  },
}));

vi.mock('../../shared/bridge/bridgeClient', () => ({
  invoke: invokeMock,
  invokeCancellable: (module: string, action: string, payload: unknown) => ({ requestId: 'request-id', promise: invokeMock(module, action, payload), cancel: vi.fn() }),
  BridgeInvokeError: BridgeInvokeErrorMock,
  BridgeCancelledError: class extends Error {},
  BridgeTimeoutError: class extends Error {},
  BridgeUnavailableError: class extends Error {},
  subscribe: () => () => {},
}));

function LocationProbe() {
  const location = useLocation();
  return <output data-testid="location">{location.pathname}{location.search}</output>;
}

function renderAt(host: string, section?: string, state?: unknown) {
  const query = section === undefined ? '' : `?section=${encodeURIComponent(section)}`;
  const entry = state === undefined
    ? `/clients/${encodeURIComponent(host)}${query}`
    : { pathname: `/clients/${encodeURIComponent(host)}`, search: query, state };
  return render(
    <MemoryRouter initialEntries={[entry]}>
      <TargetProvider>
        <EnvironmentProvider>
          <Routes>
            <Route path="/clients/:host" element={<ClientDetailPage />} />
            <Route path="/clients" element={<span>Clients list</span>} />
          </Routes>
          <LocationProbe />
        </EnvironmentProvider>
      </TargetProvider>
    </MemoryRouter>,
  );
}

const capturedInventory: HardwareInfoResult = {
  host: 'PC1',
  capturedAtUtc: '2026-08-19T10:00:00Z',
  fromCache: false,
  snapshot: {
    cpu: { name: 'Test CPU', physicalCores: 4, logicalProcessors: 8, maxClockSpeedMhz: 4000 },
    memoryBanks: [],
    disks: [],
    operatingSystem: { caption: 'Windows 11', version: '10.0', buildNumber: '26200', architecture: '64-bit' },
    networkAdapters: [],
    gpus: [],
    monitors: [],
    installedSoftware: [],
    installedSoftwareError: null,
    userEvidence: null,
  },
};

const completedSecurityScan: SecurityScanResult = {
  scanId: 1,
  host: 'PC1',
  startedAtUtc: '2026-08-19T10:05:00Z',
  completedAtUtc: '2026-08-19T10:05:05Z',
  status: 'COMPLETED',
  findings: [],
  checkResults: [],
  coverageVersion: 1,
  coverage: {
    isKnown: true,
    totalChecks: 13,
    succeededChecks: 13,
    failedChecks: 0,
    requiresElevationChecks: 0,
    notApplicableChecks: 0,
    applicableChecks: 13,
    isComplete: true,
  },
};

const storedClientOverview: ClientOverviewResult = {
  host: 'PC1.corp.local',
  inventory: null,
  software: null,
  health: null,
  security: null,
  users: null,
  sources: [
    { source: 'Inventory', provenance: 'Persisted WMI/CIM hardware snapshot', freshness: 'MISSING', capturedAtUtc: null, ageSeconds: null, isComplete: false, coverage: 'No stored hardware snapshot.', detailSection: 'inventory' },
    { source: 'Installed software', provenance: 'Persisted Inventory software capture', freshness: 'MISSING', capturedAtUtc: null, ageSeconds: null, isComplete: false, coverage: 'No stored software capture.', detailSection: 'inventory' },
    { source: 'Health', provenance: 'Latest persisted on-demand Health run', freshness: 'MISSING', capturedAtUtc: null, ageSeconds: null, isComplete: false, coverage: 'No stored Health run.', detailSection: 'diagnostics' },
    { source: 'Security', provenance: 'Latest persisted Security scan and per-check coverage', freshness: 'MISSING', capturedAtUtc: null, ageSeconds: null, isComplete: false, coverage: 'No stored Security scan.', detailSection: 'security' },
    { source: 'Linked users', provenance: 'Latest stored WEC Inventory user evidence', freshness: 'MISSING', capturedAtUtc: null, ageSeconds: null, isComplete: false, coverage: 'No stored user/device relationship evidence.', detailSection: 'inventory' },
  ],
};

function reportOverview(inventoryAvailable: boolean, securityAvailable: boolean): ReportOverview {
  return {
    subjectHost: 'PC-42',
    inventoryCapturedAtUtc: inventoryAvailable ? capturedInventory.capturedAtUtc : null,
    securityScanCompletedAtUtc: securityAvailable ? completedSecurityScan.completedAtUtc : null,
    securityScanStatus: securityAvailable ? completedSecurityScan.status : null,
    securityFindingCount: securityAvailable ? completedSecurityScan.findings.length : null,
    securityCoverage: securityAvailable ? completedSecurityScan.coverage : null,
    readiness: {
      evaluatedAtUtc: '2026-08-19T10:06:00Z',
      isReady: inventoryAvailable && securityAvailable,
      sources: [
        {
          source: 'Hardware inventory',
          provenance: 'Persisted snapshot',
          state: inventoryAvailable ? 'READY' : 'MISSING',
          capturedAtUtc: inventoryAvailable ? capturedInventory.capturedAtUtc : null,
          ageSeconds: inventoryAvailable ? 360 : null,
          isComplete: inventoryAvailable,
          summary: inventoryAvailable ? 'Inventory is current.' : 'No inventory data is available.',
        },
        {
          source: 'Security posture',
          provenance: 'Persisted scan',
          state: securityAvailable ? 'READY' : 'MISSING',
          capturedAtUtc: securityAvailable ? completedSecurityScan.completedAtUtc : null,
          ageSeconds: securityAvailable ? 55 : null,
          isComplete: securityAvailable,
          summary: securityAvailable ? 'Security scan is current.' : 'No security data is available.',
        },
      ],
    },
  };
}

describe('ClientDetailPage', () => {
  beforeEach(() => {
    invokeMock.mockReset();
    invokeMock.mockImplementation((module: string, action: string) => {
      if (module === 'system' && action === 'getAppInfo') {
        return Promise.resolve({
          version: 'x',
          databasePath: '',
          logDirectory: '',
          isElevated: false,
          maxParallelScans: 4,
          machineName: 'WEC-HOST',
          machineFqdn: 'WEC-HOST.corp.example',
          runtimeProfile: 'test',
        });
      }
      if (module === 'inventory' && action === 'getHardwareInfo') {
        return Promise.reject(new BridgeInvokeErrorMock({ code: 'NOT_FOUND', message: 'No cached snapshot.' }));
      }
      if (module === 'clients' && action === 'getOverview') {
        return Promise.resolve(storedClientOverview);
      }
      if (module === 'employeelifecycle' && action === 'getHygiene') {
        return Promise.resolve({
          assessedAtUtc: '2026-08-18T08:00:00Z', domainName: 'corp.local',
          sources: {
            activeDirectory: { availability: 'AVAILABLE', error: null },
            kaspersky: { availability: 'AVAILABLE', error: null },
            opsi: { availability: 'UNAVAILABLE', error: 'opsi service unavailable' },
          },
          summary: { total: 1, adComputers: 1, kasperskyComputers: 0, opsiComputers: 0, healthy: 0, problems: 1, incomplete: 0, stale: 0, missingKaspersky: 1, orphanKaspersky: 0, missingOpsi: 0, orphanOpsi: 0, outdated: 0 },
          devices: [{
            computerName: 'PC1', hostName: 'PC1.corp.local',
            activeDirectory: { exists: true, enabled: true, dnsHostName: 'PC1.corp.local', operatingSystem: 'Windows 11 Pro', description: 'Test notebook', distinguishedName: 'CN=PC1,OU=Clients,DC=corp,DC=local', organizationalUnit: 'OU=Clients,DC=corp,DC=local', lastLogonDate: '2026-08-17T08:00:00Z' },
            kaspersky: { exists: false, lastSeen: null, agentVersion: null, kesVersion: null, administrationGroup: null },
            opsi: { exists: false, clientId: null, description: null, depotId: null, lastSeen: null, clientAgentVersion: null },
            assessment: { status: 'WARNING', findings: [{ code: 'MISSING_KASPERSKY', severity: 'WARNING', message: 'Enabled in Active Directory, but no matching Kaspersky device was found.' }] },
          }],
        });
      }
      if (module === 'security' && action === 'getLatestScan') return Promise.resolve({ scan: null });
      if (module === 'system' && action === 'openPsSession') return Promise.reject(new Error('raw PowerShell provider failure'));
      if (module === 'reporting' && action === 'getOverview') {
        return Promise.resolve({
          inventoryCapturedAtUtc: null,
          securityScanCompletedAtUtc: null,
          securityScanStatus: null,
          securityFindingCount: null,
          securityCoverage: null,
          readiness: {
            evaluatedAtUtc: '2026-08-18T08:00:00Z',
            isReady: false,
            sources: [
              { source: 'Hardware inventory', provenance: 'Persisted snapshot', state: 'MISSING', capturedAtUtc: null, ageSeconds: null, isComplete: false, summary: 'No inventory data is available.' },
              { source: 'Security posture', provenance: 'Persisted scan', state: 'MISSING', capturedAtUtc: null, ageSeconds: null, isComplete: false, summary: 'No Security data is available.' },
            ],
          },
        });
      }
      return Promise.resolve({ targets: [] });
    });
  });

  it('opens cached inventory without querying BitLocker until explicitly requested', async () => {
    const fallback = invokeMock.getMockImplementation()!;
    invokeMock.mockImplementation((module: string, action: string, payload: unknown) => {
      if (module === 'inventory' && action === 'getHardwareInfo') return Promise.resolve({ ...capturedInventory, fromCache: true });
      if (module === 'inventory' && action === 'getDiskEncryptionStatus') return Promise.resolve({ volumes: [] });
      return fallback(module, action, payload);
    });
    renderAt('PC1.corp.local');
    await screen.findByText('Test CPU');
    expect(invokeMock.mock.calls.filter((call) => call[1] === 'getDiskEncryptionStatus')).toHaveLength(0);
    fireEvent.click(screen.getByRole('tab', { name: 'Inventory' }));
    expect(invokeMock.mock.calls.filter((call) => call[1] === 'getDiskEncryptionStatus')).toHaveLength(0);
    fireEvent.click(screen.getByRole('button', { name: 'Check BitLocker' }));
    await screen.findByText('No encryptable volumes found.');
    expect(invokeMock.mock.calls.filter((call) => call[1] === 'getDiskEncryptionStatus')).toHaveLength(1);
  });

  it('shows a credential bar for a remote client and runs each section on demand', async () => {
    renderAt('PC1.corp.local');

    expect(await screen.findByText('Remote account: current Windows user')).toBeDefined(); // remote → creds needed
    expect((await screen.findByRole('tab', { name: 'Overview' })).getAttribute('aria-selected')).toBe('true');
    expect(await screen.findByText('Client overview')).toBeDefined();
    expect(invokeMock.mock.calls.some((call) => call[0] === 'employeelifecycle')).toBe(false);
    fireEvent.click(screen.getByRole('button', { name: 'Load management sources' }));
    expect(await screen.findByText('Environment assessment')).toBeDefined();
    expect(screen.getByText('Warning')).toBeDefined();
    fireEvent.click(screen.getByRole('button', { name: 'PowerShell' }));
    const alert = (await screen.findByText('The PowerShell session could not be opened.')).closest<HTMLElement>('[role="alert"]')!;
    expect(within(alert).getByText('The PowerShell session could not be opened.')).toBeDefined();
    expect(within(alert).getByText('Next action')).toBeDefined();
    const details = within(alert).getByText('Technical details').closest('details') as HTMLDetailsElement;
    expect(details.open).toBe(false);
    fireEvent.click(details.querySelector('summary')!);
    expect(within(alert).getByText(/raw PowerShell provider failure/)).toBeDefined();
    expect(screen.getByText('MISSING KASPERSKY').parentElement?.textContent)
      .toContain('Enabled in Active Directory, but no matching Kaspersky device was found.');
    expect(screen.getByText('Windows 11 Pro')).toBeDefined();
    expect(screen.getByText('opsi service unavailable').closest('details')?.open).toBe(false);
    expect(screen.getAllByText('Failed').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Source unavailable').length).toBeGreaterThan(0);
    expect(screen.queryByText('Present')).toBeNull();
    expect(screen.getAllByText('Available').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Missing').length).toBeGreaterThan(0);
    fireEvent.click(screen.getByRole('tab', { name: 'Inventory' }));
    expect(await screen.findByText('Run inventory scan')).toBeDefined(); // no cache → on-demand

    fireEvent.click(screen.getByRole('tab', { name: 'Security' }));
    expect(await screen.findByText('Run security scan')).toBeDefined();

    fireEvent.click(screen.getByRole('tab', { name: 'Health' }));
    expect(await screen.findByText('Run health check')).toBeDefined();

    fireEvent.click(screen.getByRole('tab', { name: 'Report export' }));
    // Remote reporting now works: the section reports on the scanned host by name
    expect(await screen.findByText('Included data — PC1.corp.local')).toBeDefined();
    expect(await screen.findByRole('button', { name: 'Export HTML' })).toBeDefined();
  });

  it('refreshes stored overview and report after successful inventory and security scans', async () => {
    let inventoryAvailable = false;
    let securityAvailable = false;
    const fallback = invokeMock.getMockImplementation() as (
      module: string,
      action: string,
      payload?: unknown,
    ) => Promise<unknown>;

    invokeMock.mockImplementation((module: string, action: string, payload?: unknown) => {
      if (module === 'inventory' && action === 'getHardwareInfo') {
        if ((payload as { cacheOnly?: boolean }).cacheOnly) {
          return Promise.reject(new BridgeInvokeErrorMock({ code: 'NOT_FOUND', message: 'No cached snapshot.' }));
        }
        inventoryAvailable = true;
        return Promise.resolve(capturedInventory);
      }
      if (module === 'inventory' && action === 'getDiskEncryptionStatus') {
        return Promise.resolve({ volumes: [] });
      }
      if (module === 'security' && action === 'getLatestScan') {
        return Promise.resolve({ scan: null });
      }
      if (module === 'security' && action === 'getScanHistory') {
        return Promise.resolve({ scans: [], changesSinceLastScan: null });
      }
      if (module === 'security' && action === 'runScan') {
        securityAvailable = true;
        return Promise.resolve(completedSecurityScan);
      }
      if (module === 'reporting' && action === 'getOverview') {
        return Promise.resolve(reportOverview(inventoryAvailable, securityAvailable));
      }
      return fallback(module, action, payload);
    });

    renderAt('PC1.corp.local');

    await waitFor(() => {
      expect(invokeMock).toHaveBeenCalledWith(
        'reporting',
        'getOverview',
        { host: 'PC1.corp.local' },
      );
    });

    fireEvent.click(await screen.findByRole('tab', { name: 'Inventory' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Run inventory scan' }));
    expect(await screen.findByText('Test CPU')).toBeDefined();

    fireEvent.click(screen.getByRole('tab', { name: 'Report export' }));
    expect(await screen.findByText(/Snapshot from/)).toBeDefined();

    fireEvent.click(screen.getByRole('tab', { name: 'Security' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Run security scan' }));
    expect(await screen.findByText('No findings — all applicable checks completed.')).toBeDefined();

    fireEvent.click(screen.getByRole('tab', { name: 'Report export' }));
    expect(await screen.findByText(/COMPLETED, 0 findings/)).toBeDefined();
    await waitFor(() => expect(invokeMock.mock.calls.filter((call) => call[0] === 'clients' && call[1] === 'getOverview')).toHaveLength(3));
    expect(invokeMock.mock.calls.some((call) => call[0] === 'employeelifecycle' || call[0] === 'connectivity')).toBe(false);
  });

  it('updates the stored overview after Health finishes without launching another scan', async () => {
    let completed = false;
    const fallback = invokeMock.getMockImplementation()!;
    invokeMock.mockImplementation((module: string, action: string, payload: unknown) => {
      if (module === 'diagnostics' && action === 'runDiagnostics') {
        completed = true;
        return Promise.resolve({ startedAtUtc: '2026-09-08T08:00:00Z', completedAtUtc: '2026-09-08T08:00:01Z', results: [] });
      }
      if (module === 'clients' && action === 'getOverview') return Promise.resolve({
        ...storedClientOverview,
        sources: storedClientOverview.sources.map(source => source.source === 'Health' && completed
          ? { ...source, freshness: 'FRESH', capturedAtUtc: '2026-09-08T08:00:01Z', ageSeconds: 0, coverage: 'Updated Health evidence' }
          : source),
      });
      return fallback(module, action, payload);
    });
    renderAt('PC1.corp.local', 'diagnostics');
    fireEvent.click(await screen.findByRole('button', { name: 'Run health check' }));
    await screen.findByRole('button', { name: 'Re-run health check' });
    fireEvent.click(screen.getByRole('tab', { name: 'Overview' }));
    await screen.findByText('Updated Health evidence');
    expect(invokeMock.mock.calls.filter(call => call[1] === 'runDiagnostics')).toHaveLength(1);
    expect(invokeMock.mock.calls.filter(call => call[0] === 'clients' && call[1] === 'getOverview')).toHaveLength(2);
    expect(invokeMock.mock.calls.some(call => call[0] === 'employeelifecycle' || call[0] === 'connectivity')).toBe(false);
  });

  it('opens an allowlisted client section from the URL and keeps tab navigation addressable', async () => {
    renderAt('PC1.corp.local', 'security');

    expect((await screen.findByRole('tab', { name: 'Security' })).getAttribute('aria-selected')).toBe('true');
    expect(screen.getByRole('tab', { name: 'Overview' }).getAttribute('aria-selected')).toBe('false');
    expect(invokeMock.mock.calls.some((call) => call[1] === 'runScan')).toBe(false);

    fireEvent.click(screen.getByRole('tab', { name: 'Inventory' }));
    expect(screen.getByTestId('location').textContent)
      .toBe('/clients/PC1.corp.local?section=inventory');

    fireEvent.click(screen.getByRole('tab', { name: 'Overview' }));
    expect(screen.getByTestId('location').textContent).toBe('/clients/PC1.corp.local');
  });

  it('explains local target storage, suppresses duplicate saves and reports failure', async () => {
    const fallback = invokeMock.getMockImplementation()!;
    let rejectSave!: (reason: unknown) => void;
    const pendingSave = new Promise((_resolve, reject) => { rejectSave = reject; });
    invokeMock.mockImplementation((module: string, action: string, payload: unknown) => {
      if (module === 'targets' && action === 'save') return pendingSave;
      return fallback(module, action, payload);
    });

    renderAt('PC1.corp.local');
    expect(await screen.findByText(/stores this host, label, role and optional username in WEC's local database/)).toBeDefined();
    expect(screen.getByText(/Session passwords are never saved/)).toBeDefined();

    await userEvent.dblClick(screen.getByRole('button', { name: 'Save client' }));
    const saveCalls = invokeMock.mock.calls.filter((call) => call[0] === 'targets' && call[1] === 'save');
    expect(saveCalls).toHaveLength(1);
    expect(saveCalls[0][2]).not.toHaveProperty('password');

    rejectSave(new Error('local database unavailable'));
    expect(await screen.findByText('The client target could not be saved locally.')).toBeDefined();
    expect(screen.getByRole('button', { name: 'Save client' })).toBeDefined();
  });

  it('reports a failed local unsave without removing the visible target', async () => {
    const fallback = invokeMock.getMockImplementation()!;
    invokeMock.mockImplementation((module: string, action: string, payload: unknown) => {
      if (module === 'targets' && action === 'list') {
        return Promise.resolve({ targets: [{
          id: 17,
          label: 'PC1.corp.local',
          host: 'PC1.corp.local',
          role: 'Client',
          userName: null,
          createdAtUtc: '2026-09-08T10:00:00Z',
        }] });
      }
      if (module === 'targets' && action === 'delete') return Promise.reject(new Error('delete failed'));
      return fallback(module, action, payload);
    });

    renderAt('PC1.corp.local');
    await userEvent.click(await screen.findByRole('button', { name: 'Unsave client' }));

    expect(await screen.findByText('The locally saved client target could not be removed.')).toBeDefined();
    expect(screen.getByRole('button', { name: 'Unsave client' })).toBeDefined();
  });

  it('keeps the complete Clients return URL after switching detail tabs', async () => {
    const returnTo = '/clients?q=PC&posture=OUTDATED&source=OPSI&page=2&sort=overall&direction=desc';
    renderAt('PC1.corp.local', undefined, {
      returnTo,
      scope: clientListScope({ activeDirectory: {}, kaspersky: null }),
      probeStates: {},
      selectedHosts: [],
      scrollTop: 240,
    });

    await screen.findByRole('tab', { name: 'Overview' });
    fireEvent.click(screen.getByRole('tab', { name: 'Security' }));
    fireEvent.click(screen.getByRole('button', { name: /All clients/ }));

    expect(screen.getByTestId('location').textContent).toBe(returnTo);
  });

  it('falls back safely to Overview for an unknown section URL', async () => {
    renderAt('PC1.corp.local', 'not-a-section');

    expect((await screen.findByRole('tab', { name: 'Overview' })).getAttribute('aria-selected')).toBe('true');
    expect(screen.getByRole('tab', { name: 'Inventory' }).getAttribute('aria-selected')).toBe('false');
  });
});
