import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation, useNavigate } from 'react-router-dom';
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
    key: host.toUpperCase(),
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
  const grouped = payload.groupMode === 'os' || payload.groupMode === 'site';
  const groups = grouped
    ? [...filtered.reduce((byLabel, row) => {
      const label = row.groupLabel ?? 'Unknown';
      byLabel.set(label, [...(byLabel.get(label) ?? []), row]);
      return byLabel;
    }, new Map<string, ClientWorkspaceListItem[]>()).values()]
    : [];
  const items = grouped
    ? groups.slice((page - 1) * pageSize, page * pageSize).flat()
    : filtered.slice((page - 1) * pageSize, page * pageSize);
  return {
    items,
    total: filtered.length,
    scannedTotal: filtered.filter((row) => row.scanned).length,
    snapshotTotal: rows.length,
    page,
    pageSize,
    groupCount: grouped ? groups.length : null,
    snapshotRevision: 2,
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

function ReturnToClients() {
  const location = useLocation();
  const navigate = useNavigate();
  return <button type="button" onClick={() => navigate('/clients', { state: location.state })}>Return to clients</button>;
}

function renderPage(initialEntry = '/clients') {
  return render(
    <MemoryRouter initialEntries={[initialEntry]}>
      <TargetProvider>
        <EnvironmentProvider>
          <LocationProbe />
          <Routes>
            <Route path="/clients" element={<ClientsPage />} />
            <Route path="/clients/:host" element={<ReturnToClients />} />
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
      if (module === 'system' && action === 'getAppInfo') return Promise.resolve({ maxBatchHosts: 50 });
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

  it('runs a batch only after explicit client selection and shows typed per-host failures', async () => {
    invokeMock.mockImplementation((module: string, action: string, payload: Record<string, unknown> = {}) => {
      if (module === 'targets' && action === 'list') return Promise.resolve({ targets: [] });
      if (module === 'system' && action === 'getAppInfo') return Promise.resolve({ maxBatchHosts: 2 });
      if (module === 'employeelifecycle' && action === 'listClientWorkspace') return Promise.resolve(pageFor(payload));
      if (module === 'inventory' && action === 'runBatchScan') return Promise.resolve({
        startedAtUtc: '2026-08-27T08:00:00Z',
        completedAtUtc: '2026-08-27T08:01:00Z',
        hosts: [
          {
            host: 'pc01.corp.local',
            status: 'FAILED',
            inventory: null,
            error: {
              host: 'pc01.corp.local',
              phase: 'CONNECT',
              code: 'WIN_RM_UNAVAILABLE',
              message: 'WinRM did not answer.',
              details: null,
            },
          },
        ],
      });
      return Promise.reject(new Error(`Unexpected action ${module}/${action}`));
    });

    renderPage();
    await screen.findByText('PC01');
    expect(lastInvoke('runBatchScan')).toBeUndefined();

    await userEvent.click(screen.getByRole('checkbox', { name: 'Select PC01 for bulk scan' }));
    expect(screen.getByText('1 of 2 hosts selected')).toBeTruthy();
    expect(screen.getByText(/Current Windows identity · read-only/)).toBeTruthy();
    await userEvent.click(screen.getByRole('button', { name: 'Run Inventory' }));

    await waitFor(() => expect(lastInvoke('runBatchScan')?.[2]).toEqual({ hosts: ['pc01.corp.local'] }));
    expect(await screen.findByText(/WinRM did not answer/)).toBeTruthy();
    expect(screen.getByRole('link', { name: 'pc01.corp.local' }).getAttribute('href'))
      .toBe('/clients/pc01.corp.local?section=inventory');
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
        const critical = device('PC01', { opsi: true });
        critical.nessus.critical = 3;
        critical.assessment = {
          status: 'CRITICAL',
          findings: [{ code: 'NESSUS_CRITICAL_VULNERABILITIES', severity: 'CRITICAL', message: 'Nessus reports 3 critical finding instances.' }],
        };
        return Promise.resolve({
          ...pageFor(payload, [item(critical, 'PC01'), item(device('DISABLED-PC', { enabled: false }), 'DISABLED-PC')]),
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
    expect(within(pcRow!).getByText('Critical · 3 instances')).toBeTruthy();
    expect(within(pcRow!).getByText('Partial coverage')).toBeTruthy();
    expect(within(pcRow!).getByText(/^Scan /)).toBeTruthy();
    expect(within(disabledRow!).getByText('Partial')).toBeTruthy();
    expect(screen.getByRole('group', { name: 'Nessus: Partial' })).toBeTruthy();
    expect(screen.getByText(/Counts are known results only/)).toBeTruthy();
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

  it('offers focused cleanup filters for stale and missing AD clients', async () => {
    renderPage();
    await screen.findByText('Fleet posture');

    const filter = screen.getByLabelText('Filter clients by posture') as HTMLSelectElement;
    expect([...filter.options].map((option) => option.value)).toEqual(expect.arrayContaining([
      'STALE_AD', 'STALE_KASPERSKY', 'STALE_OPSI', 'MISSING_AD', 'DISABLED_AD',
    ]));
    await userEvent.selectOptions(filter, 'MISSING_AD');
    await waitFor(() => expect(lastInvoke('listClientWorkspace')?.[2]).toMatchObject({ statusFilter: 'MISSING_AD', page: 1 }));
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
    expect(await screen.findByRole('button', { name: 'Return to clients' })).toBeTruthy();
  });

  it('collapses OS groups until their entries are requested', async () => {
    const grouped = [
      item(device('PC01', { opsi: true }), 'PC01', { groupLabel: 'Windows 11 Pro', groupTotal: 2 }),
      item(device('PC02'), 'PC02', { groupLabel: 'Windows 11 Pro', groupTotal: 2 }),
      item(device('SERVER01'), 'SERVER01', { groupLabel: 'Windows Server 2025', groupTotal: 1 }),
    ];
    invokeMock.mockImplementation((module: string, action: string, payload: Record<string, unknown> = {}) => {
      if (module === 'targets' && action === 'list') return Promise.resolve({ targets: [] });
      if (module === 'employeelifecycle' && action === 'listClientWorkspace') return Promise.resolve(pageFor(payload, grouped));
      return Promise.reject(new Error(`Unexpected action ${module}/${action}`));
    });

    renderPage();
    await screen.findByText('PC01');
    await userEvent.selectOptions(screen.getByLabelText('Group clients by'), 'os');

    const windowsGroup = await screen.findByRole('button', { name: /Windows 11 Pro \(2\)/ });
    expect(windowsGroup.getAttribute('aria-expanded')).toBe('false');
    expect(screen.queryByText('PC01')).toBeNull();
    await userEvent.click(windowsGroup);
    expect(await screen.findByText('PC01')).toBeTruthy();
    expect(windowsGroup.getAttribute('aria-expanded')).toBe('true');
  });

  it('keeps connection evidence when returning from a client detail', async () => {
    renderPage();
    await screen.findByText('PC01');
    await userEvent.click(screen.getByRole('button', { name: 'Check page connectivity' }));
    expect((await screen.findAllByText('Ping + WinRM 5985')).length).toBe(3);

    await userEvent.click(screen.getByText('PC01'));
    await userEvent.click(screen.getByRole('button', { name: 'Return to clients' }));

    expect((await screen.findAllByText('Ping + WinRM 5985')).length).toBe(3);
  });

  it('shows a registered Kaspersky computer without Agent and KES as missing', async () => {
    const withoutComponents = device('PC01');
    withoutComponents.kaspersky.agentVersion = null;
    withoutComponents.kaspersky.kesVersion = null;
    withoutComponents.assessment = {
      status: 'WARNING',
      findings: [
        { code: 'MISSING_KASPERSKY_AGENT', severity: 'WARNING', message: 'Agent missing' },
        { code: 'MISSING_KES', severity: 'WARNING', message: 'KES missing' },
      ],
    };
    invokeMock.mockImplementation((module: string, action: string, payload: Record<string, unknown> = {}) => {
      if (module === 'targets' && action === 'list') return Promise.resolve({ targets: [] });
      if (module === 'employeelifecycle' && action === 'listClientWorkspace') return Promise.resolve(pageFor(payload, [item(withoutComponents, 'PC01')]));
      return Promise.reject(new Error(`Unexpected action ${module}/${action}`));
    });

    renderPage();

    const row = (await screen.findByText('PC01')).closest('tr');
    expect(row).not.toBeNull();
    expect(within(row!).getByText('Missing')).toBeTruthy();
    expect(within(row!).getByText('Network Agent + KES not installed')).toBeTruthy();
  });

  it('shows an unscanned Nessus asset as missing instead of critical', async () => {
    const unscanned = device('PC01');
    unscanned.nessus.lastCompletedScanUtc = null;
    unscanned.nessus.critical = 2;
    unscanned.assessment = {
      status: 'WARNING',
      findings: [{ code: 'MISSING_NESSUS', severity: 'WARNING', message: 'No scan' }],
    };
    invokeMock.mockImplementation((module: string, action: string, payload: Record<string, unknown> = {}) => {
      if (module === 'targets' && action === 'list') return Promise.resolve({ targets: [] });
      if (module === 'employeelifecycle' && action === 'listClientWorkspace') return Promise.resolve(pageFor(payload, [item(unscanned, 'PC01')]));
      return Promise.reject(new Error(`Unexpected action ${module}/${action}`));
    });

    renderPage();

    const row = (await screen.findByText('PC01')).closest('tr');
    expect(row).not.toBeNull();
    expect(within(row!).getByText('Missing')).toBeTruthy();
    expect(within(row!).getByText('No completed scan')).toBeTruthy();
    expect(within(row!).queryByText('Critical')).toBeNull();
  });

  it('renders an opsi stale finding as Stale rather than Available', async () => {
    const staleOpsi = device('PC01', { opsi: true });
    staleOpsi.assessment = {
      status: 'WARNING',
      findings: [{ code: 'STALE_OPSI', severity: 'WARNING', message: 'opsi stale' }],
    };
    invokeMock.mockImplementation((module: string, action: string, payload: Record<string, unknown> = {}) => {
      if (module === 'targets' && action === 'list') return Promise.resolve({ targets: [] });
      if (module === 'employeelifecycle' && action === 'listClientWorkspace') return Promise.resolve(pageFor(payload, [item(staleOpsi, 'PC01')]));
      return Promise.reject(new Error(`Unexpected action ${module}/${action}`));
    });

    renderPage();

    const row = (await screen.findByText('PC01')).closest('tr');
    expect(row).not.toBeNull();
    expect(within(row!).getByText('Stale')).toBeTruthy();
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
