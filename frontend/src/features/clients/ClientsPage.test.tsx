import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ClientWorkspaceListItem, ClientWorkspacePage, EnvironmentSourceStates, HygieneDevice, HygieneSummary, ProbeHostsResult } from '../../shared/api-types';
import { EnvironmentProvider } from '../../shared/environment/EnvironmentContext';
import { TargetProvider } from '../../shared/targets/TargetContext';
import { ClientsPage } from './ClientsPage';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../shared/bridge/bridgeClient', () => ({
  invoke: invokeMock,
  invokeCancellable: (module: string, action: string, payload: unknown) => ({
    requestId: 'request-id',
    promise: invokeMock(module, action, payload),
    cancel: vi.fn(),
  }),
  subscribe: vi.fn(() => () => {}),
  BridgeInvokeError: class extends Error {},
  BridgeCancelledError: class extends Error {},
  BridgeTimeoutError: class extends Error {},
  BridgeUnavailableError: class extends Error {},
}));

const sources: EnvironmentSourceStates = {
  activeDirectory: { availability: 'AVAILABLE', error: null },
  kaspersky: { availability: 'AVAILABLE', error: null },
  opsi: { availability: 'AVAILABLE', error: null },
  nessus: { availability: 'AVAILABLE', error: null },
};

const postureSummary: HygieneSummary = {
  total: 3,
  adComputers: 2,
  kasperskyComputers: 1,
  opsiComputers: 1,
  nessusComputers: 1,
  healthy: 1,
  problems: 1,
  incomplete: 1,
  stale: 1,
  missingKaspersky: 1,
  orphanKaspersky: 0,
  missingOpsi: 1,
  orphanOpsi: 0,
  outdated: 1,
  missingNessus: 1,
  staleNessus: 0,
  nessusCritical: 1,
  nessusHigh: 1,
};

function device(name: string, options: { enabled?: boolean; opsi?: boolean } = {}): HygieneDevice {
  const host = `${name.toLowerCase()}.corp.local`;
  return {
    computerName: name,
    hostName: host,
    activeDirectory: { exists: true, enabled: options.enabled ?? true, dnsHostName: host, operatingSystem: 'Windows 11 Pro', description: null, distinguishedName: `CN=${name},DC=corp,DC=local`, organizationalUnit: 'DC=corp,DC=local', lastLogonDate: '2026-08-19T08:30:00Z' },
    kaspersky: { exists: name === 'PC01', lastSeen: name === 'PC01' ? '2026-08-18T07:15:00Z' : null, agentVersion: name === 'PC01' ? '16.0' : null, kesVersion: name === 'PC01' ? '21.25' : null, administrationGroup: name === 'PC01' ? 'Clients' : null },
    opsi: { exists: options.opsi ?? false, clientId: options.opsi ? host : null, description: null, depotId: options.opsi ? 'depot01' : null, lastSeen: options.opsi ? '2026-08-17T06:45:00Z' : null, clientAgentVersion: options.opsi ? '4.3.8' : null },
    nessus: { exists: name === 'PC01', assetId: name === 'PC01' ? 'asset-1' : null, ipAddress: name === 'PC01' ? '10.0.0.1' : null, lastCompletedScanUtc: name === 'PC01' ? '2026-08-18T05:00:00Z' : null, critical: 0, high: 0, medium: name === 'PC01' ? 1 : 0, low: name === 'PC01' ? 2 : 0, info: 0, ports: name === 'PC01' ? [443] : [], scanSources: name === 'PC01' ? ['Clients'] : [] },
    assessment: { status: 'HEALTHY', findings: [] },
  };
}

function item(environment: HygieneDevice | null, fallback: string, overrides: Partial<ClientWorkspaceListItem> = {}): ClientWorkspaceListItem {
  const host = environment?.hostName ?? fallback;
  return {
    host,
    key: host.split('.')[0].toUpperCase(),
    name: environment?.computerName ?? fallback,
    os: environment?.activeDirectory.operatingSystem ?? null,
    description: null,
    enabled: environment?.activeDirectory.enabled ?? true,
    scanned: false,
    capturedAtUtc: null,
    saved: false,
    inAd: environment?.activeDirectory.exists ?? false,
    environment,
    groupLabel: null,
    groupTotal: null,
    ...overrides,
  };
}

const allItems = [
  item(device('DISABLED-PC', { enabled: false }), 'DISABLED-PC'),
  item(device('PC01', { opsi: true }), 'PC01'),
  item(null, 'SCAN-ONLY', { scanned: true, capturedAtUtc: '2026-08-17T06:00:00Z' }),
];

function pageFor(payload: Record<string, unknown>, rows: ClientWorkspaceListItem[] = allItems): ClientWorkspacePage {
  let filtered = rows;
  if (payload.sourceFilter === 'OPSI') filtered = filtered.filter((row) => row.environment?.opsi.exists);
  if (payload.sourceFilter === 'SCANNED') filtered = filtered.filter((row) => row.scanned);
  const page = Number(payload.page ?? 1);
  const pageSize = Number(payload.pageSize ?? 50);
  const items = filtered.slice((page - 1) * pageSize, page * pageSize);
  return {
    items,
    total: filtered.length,
    scannedTotal: filtered.filter((row) => row.scanned).length,
    snapshotTotal: rows.length,
    page,
    pageSize,
    assessedAtUtc: '2026-08-19T10:00:00Z',
    domainName: 'corp.local',
    summary: postureSummary,
    sources,
  };
}

function LocationProbe() {
  const location = useLocation();
  return <output data-testid="location">{location.pathname}{location.search}</output>;
}

function renderPage(initialEntry = '/clients') {
  return render(
    <MemoryRouter initialEntries={[initialEntry]}>
      <TargetProvider>
        <EnvironmentProvider>
          <LocationProbe />
          <Routes>
            <Route path="/clients" element={<ClientsPage />} />
            <Route path="/clients/:host" element={<div>Shared client detail</div>} />
          </Routes>
        </EnvironmentProvider>
      </TargetProvider>
    </MemoryRouter>,
  );
}

function lastInvoke(action: string): unknown[] | undefined {
  const calls = invokeMock.mock.calls as unknown[][];
  return [...calls].reverse().find((call) => call[1] === action);
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, resolve, reject };
}

describe('ClientsPage', () => {
  beforeEach(() => {
    invokeMock.mockReset();
    invokeMock.mockImplementation((module: string, action: string, payload: Record<string, unknown> = {}) => {
      if (module === 'targets' && action === 'list') return Promise.resolve({ targets: [] });
      if (module === 'employeelifecycle' && action === 'listClientWorkspace') return Promise.resolve(pageFor(payload));
      if (module === 'connectivity' && action === 'probeHosts') return Promise.resolve({ results: (payload.hosts as string[]).map((host) => ({ host, reachable: true, manageable: true })) });
      return Promise.reject(new Error(`Unexpected action ${module}/${action}`));
    });
  });

  it('shows the compact shared inventory including disabled and unmanaged devices', async () => {
    renderPage();

    expect(await screen.findByText('DISABLED-PC')).toBeTruthy();
    expect(screen.getByText('PC01')).toBeTruthy();
    expect(screen.getByText('SCAN-ONLY')).toBeTruthy();
    for (const heading of ['Device', 'AD', 'Kaspersky', 'opsi', 'Nessus', 'Overall']) {
      expect(screen.getByRole('columnheader', { name: new RegExp(heading) })).toBeTruthy();
    }
    expect(screen.getByText('Disabled')).toBeTruthy();
    expect(screen.queryAllByText('OK')).toHaveLength(0);
    expect(screen.getAllByText('Unknown').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Unmanaged').length).toBeGreaterThan(0);
    expect(screen.getByText('1–3 of 3')).toBeTruthy();
  });

  it('shows the Active Directory description in the device column', async () => {
    const described = item(device('PC01'), 'PC01', { description: 'Accounting workstation' });
    invokeMock.mockImplementation((module: string, action: string, payload: Record<string, unknown> = {}) => {
      if (module === 'targets' && action === 'list') return Promise.resolve({ targets: [] });
      if (module === 'employeelifecycle' && action === 'listClientWorkspace') return Promise.resolve(pageFor(payload, [described]));
      return Promise.reject(new Error(`Unexpected action ${module}/${action}`));
    });

    renderPage();

    const row = (await screen.findByText('PC01')).closest('tr');
    expect(row).not.toBeNull();
    expect(within(row!).getByText('Accounting workstation')).toBeTruthy();
  });

  it('shows the Active Directory last-seen value without a connectivity probe', async () => {
    renderPage();

    const row = (await screen.findByText('PC01')).closest('tr');
    expect(row).not.toBeNull();
    expect(within(row!).getByText(/^AD Last seen /)).toBeTruthy();
    expect(within(row!).getByText(/^Kaspersky Last seen /)).toBeTruthy();
    expect(within(row!).getByText(/^opsi Last seen /)).toBeTruthy();
  });

  it('keeps showing known per-client Nessus data while the global sync is partial', async () => {
    invokeMock.mockImplementation((module: string, action: string, payload: Record<string, unknown> = {}) => {
      if (module === 'targets' && action === 'list') return Promise.resolve({ targets: [] });
      if (module === 'employeelifecycle' && action === 'listClientWorkspace') {
        return Promise.resolve({
          ...pageFor(payload),
          sources: { ...sources, nessus: { availability: 'PARTIAL' as const, error: 'Refreshing cached inventory.' } },
        });
      }
      return Promise.reject(new Error(`Unexpected action ${module}/${action}`));
    });

    renderPage();

    const pcRow = (await screen.findByText('PC01')).closest('tr');
    const disabledRow = screen.getByText('DISABLED-PC').closest('tr');
    expect(pcRow).not.toBeNull();
    expect(disabledRow).not.toBeNull();
    expect(within(pcRow!).getAllByText('Fresh')).toHaveLength(4);
    expect(within(pcRow!).queryByText('Partial')).toBeNull();
    expect(within(disabledRow!).getByText('Partial')).toBeTruthy();
    expect(screen.getByRole('group', { name: 'Nessus: Partial' })).toBeTruthy();
  });

  it('shows fleet posture and writes KPI filters to the canonical URL', async () => {
    renderPage();

    expect(await screen.findByText('Fleet posture')).toBeTruthy();
    expect(screen.getByRole('group', { name: 'AD: Available' })).toBeTruthy();
    expect(screen.getByText(/corp\.local/)).toBeTruthy();
    await userEvent.click(screen.getByRole('button', { name: 'Filter clients by Missing Kaspersky (1)' }));

    await waitFor(() => expect(screen.getByTestId('location').textContent).toBe('/clients?posture=MISSING_KASPERSKY'));
    expect(lastInvoke('listClientWorkspace')?.[2]).toMatchObject({ statusFilter: 'MISSING_KASPERSKY', page: 1 });
    expect((screen.getByLabelText('Filter clients by posture') as HTMLSelectElement).value).toBe('MISSING_KASPERSKY');
  });

  it('restores a posture drill-down from a direct URL', async () => {
    renderPage('/clients?posture=NESSUS_CRITICAL');

    await screen.findByText('Fleet posture');
    await waitFor(() => expect(lastInvoke('listClientWorkspace')?.[2]).toMatchObject({ statusFilter: 'NESSUS_CRITICAL', page: 1 }));
    expect((screen.getByLabelText('Filter clients by posture') as HTMLSelectElement).value).toBe('NESSUS_CRITICAL');
  });

  it('falls back safely when the posture URL contains an unknown value', async () => {
    renderPage('/clients?posture=UNKNOWN');

    await screen.findByText('Fleet posture');
    await waitFor(() => expect(lastInvoke('listClientWorkspace')?.[2]).toMatchObject({ statusFilter: 'ALL', page: 1 }));
    expect((screen.getByLabelText('Filter clients by posture') as HTMLSelectElement).value).toBe('ALL');
  });

  it('filters by source on the host and opens the common detail route', async () => {
    renderPage();
    await screen.findByText('PC01');

    await userEvent.selectOptions(screen.getByLabelText('Filter clients by source'), 'OPSI');
    await waitFor(() => expect(screen.queryByText('DISABLED-PC')).toBeNull());
    expect(screen.queryByText('SCAN-ONLY')).toBeNull();
    const request = lastInvoke('listClientWorkspace')?.[2];
    expect(request).toMatchObject({ sourceFilter: 'OPSI', page: 1, pageSize: 50, sortColumn: 'device', sortDirection: 'asc' });
    await userEvent.click(screen.getByText('PC01'));
    expect(await screen.findByText('Shared client detail')).toBeTruthy();
  });

  it('pages 101 clients on the host and probes only the visible page', async () => {
    const many = Array.from({ length: 101 }, (_, index) => item(null, `PC-${String(index + 1).padStart(3, '0')}`));
    invokeMock.mockImplementation((module: string, action: string, payload: Record<string, unknown> = {}) => {
      if (module === 'targets' && action === 'list') return Promise.resolve({ targets: [] });
      if (module === 'employeelifecycle' && action === 'listClientWorkspace') return Promise.resolve(pageFor(payload, many));
      if (module === 'connectivity' && action === 'probeHosts') return Promise.resolve({ results: [] });
      return Promise.reject(new Error(`Unexpected action ${module}/${action}`));
    });

    renderPage();
    expect(await screen.findByText('PC-001')).toBeTruthy();
    expect(screen.queryByText('PC-051')).toBeNull();
    await userEvent.click(screen.getByRole('button', { name: 'Next' }));
    expect(await screen.findByText('PC-051')).toBeTruthy();
    expect(screen.getByText('51–100 of 101')).toBeTruthy();

    await userEvent.click(screen.getByRole('button', { name: 'Check page connectivity' }));
    await waitFor(() => {
      const call = lastInvoke('probeHosts');
      const payload = call?.[2] as { hosts: string[] } | undefined;
      expect(payload?.hosts).toHaveLength(50);
      expect(payload?.hosts[0]).toBe('PC-051');
    });
  });

  it('replaces previous reachability with Running and then shows channel-specific evidence without Offline', async () => {
    const secondProbe = deferred<ProbeHostsResult>();
    let probeCalls = 0;
    invokeMock.mockImplementation((module: string, action: string, payload: Record<string, unknown> = {}) => {
      if (module === 'targets' && action === 'list') return Promise.resolve({ targets: [] });
      if (module === 'employeelifecycle' && action === 'listClientWorkspace') return Promise.resolve(pageFor(payload));
      if (module === 'connectivity' && action === 'probeHosts') {
        probeCalls += 1;
        if (probeCalls === 1) {
          return Promise.resolve({ results: (payload.hosts as string[]).map((host) => ({ host, reachable: true, manageable: true })) });
        }
        return secondProbe.promise;
      }
      return Promise.reject(new Error(`Unexpected action ${module}/${action}`));
    });

    renderPage();
    await screen.findByText('PC01');
    await userEvent.click(screen.getByRole('button', { name: 'Check page connectivity' }));
    expect((await screen.findAllByText('Ping + WinRM 5985')).length).toBe(3);

    await userEvent.click(screen.getByRole('button', { name: 'Check page connectivity' }));
    expect((await screen.findAllByText('Running')).length).toBe(3);
    expect(screen.queryByText('Ping + WinRM 5985')).toBeNull();
    expect(screen.queryByText('Online')).toBeNull();
    expect(screen.queryByText('Offline')).toBeNull();

    secondProbe.resolve({ results: [
      { host: 'disabled-pc.corp.local', reachable: true, manageable: true },
      { host: 'pc01.corp.local', reachable: false, manageable: true },
      { host: 'SCAN-ONLY', reachable: false, manageable: false },
    ] });

    expect(await screen.findByText('Ping + WinRM 5985')).toBeTruthy();
    expect(screen.getByText('WinRM 5985 open · No ping response')).toBeTruthy();
    expect(screen.getByText('No ping or WinRM response')).toBeTruthy();
    expect(screen.queryByText('Offline')).toBeNull();
    expect(screen.getAllByText(/Last seen /)).toHaveLength(4);
  });

  it('shows failed host states and an actionable error when the connectivity request fails', async () => {
    invokeMock.mockImplementation((module: string, action: string, payload: Record<string, unknown> = {}) => {
      if (module === 'targets' && action === 'list') return Promise.resolve({ targets: [] });
      if (module === 'employeelifecycle' && action === 'listClientWorkspace') return Promise.resolve(pageFor(payload));
      if (module === 'connectivity' && action === 'probeHosts') return Promise.reject(new Error('connectivity bridge unavailable'));
      return Promise.reject(new Error(`Unexpected action ${module}/${action}`));
    });

    renderPage();
    await screen.findByText('PC01');
    await userEvent.click(screen.getByRole('button', { name: 'Check page connectivity' }));

    const alert = await screen.findByRole('alert');
    expect(within(alert).getByText('The visible clients could not be checked.')).toBeTruthy();
    expect(within(alert).getByRole('button', { name: 'Retry connectivity check' })).toBeTruthy();
    expect(screen.getAllByText('Failed')).toHaveLength(3);
    expect(screen.queryByText('Offline')).toBeNull();
  });

  it('sends grouping, search, refresh and controlled sort state to the host', async () => {
    renderPage();
    await screen.findByText('PC01');

    await userEvent.selectOptions(screen.getByLabelText('Group clients by'), 'site');
    await userEvent.type(screen.getByLabelText('Filter clients'), 'PC');
    await userEvent.click(screen.getByRole('button', { name: /Overall/ }));
    await userEvent.click(screen.getByRole('button', { name: 'Refresh' }));

    await waitFor(() => {
      const request = lastInvoke('listClientWorkspace')?.[2];
      expect(request).toMatchObject({ groupMode: 'site', search: 'PC', page: 1, sortColumn: 'overall', sortDirection: 'asc', force: true });
    });
  });

  it('keeps client inventory failures actionable and raw evidence collapsed', async () => {
    invokeMock.mockImplementation((module: string, action: string) => {
      if (module === 'targets' && action === 'list') return Promise.resolve({ targets: [] });
      if (module === 'employeelifecycle' && action === 'listClientWorkspace') {
        return Promise.reject(new Error('provider returned raw diagnostic text'));
      }
      return Promise.reject(new Error(`Unexpected action ${module}/${action}`));
    });

    renderPage();

    const alert = await screen.findByRole('alert');
    expect(within(alert).getByText('The client inventory could not be loaded.')).toBeTruthy();
    expect(within(alert).getByText('Cause')).toBeTruthy();
    expect(within(alert).getByText('Next action')).toBeTruthy();
    expect(within(alert).getByRole('button', { name: 'Retry environment load' })).toBeTruthy();
    const details = within(alert).getByText('Technical details').closest('details') as HTMLDetailsElement;
    expect(details.open).toBe(false);

    await userEvent.click(details.querySelector('summary')!);
    expect(details.open).toBe(true);
    expect(within(alert).getByText(/provider returned raw diagnostic text/)).toBeTruthy();
  });
});
