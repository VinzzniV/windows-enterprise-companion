import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type {
  AdComputerSearchResult,
  AppInfoResponse,
  HardwareInfoResult,
  ListInventoryHostsResult,
} from '../../shared/api-types';
import { BridgeInvokeError } from '../../shared/bridge/bridgeClient';
import { TargetProvider } from '../../shared/targets/TargetContext';
import { ComparePage } from './ComparePage';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../shared/bridge/bridgeClient', async (importOriginal) => {
  const original = await importOriginal<typeof import('../../shared/bridge/bridgeClient')>();
  return { ...original, invoke: invokeMock };
});

const directory: AdComputerSearchResult = {
  domainJoined: true,
  domainName: 'corp.local',
  truncated: false,
  computers: [
    {
      name: 'PC-A',
      dnsHostName: 'pc-a.corp.local',
      operatingSystem: 'Windows 11 Pro',
      enabled: true,
      description: null,
      distinguishedName: 'CN=PC-A,DC=corp,DC=local',
      lastLogonDate: null,
    },
    {
      name: 'PC-B',
      dnsHostName: 'pc-b.corp.local',
      operatingSystem: 'Windows 11 Pro',
      enabled: true,
      description: null,
      distinguishedName: 'CN=PC-B,DC=corp,DC=local',
      lastLogonDate: null,
    },
    {
      name: 'PC-C',
      dnsHostName: 'pc-c.corp.local',
      operatingSystem: 'Windows 10 Pro',
      enabled: true,
      description: null,
      distinguishedName: 'CN=PC-C,DC=corp,DC=local',
      lastLogonDate: null,
    },
  ],
};

const inventoryHosts: ListInventoryHostsResult = {
  hosts: [
    { host: 'pc-a.corp.local', capturedAtUtc: '2026-08-18T08:00:00Z' },
    { host: 'pc-b.corp.local', capturedAtUtc: '2026-08-18T08:05:00Z' },
  ],
};

const securityHosts = {
  hosts: [
    { host: 'pc-b.corp.local', completedAtUtc: '2026-08-18T09:05:00Z' },
  ],
};

const appInfo: AppInfoResponse = {
  version: '1.0.0',
  databasePath: 'wec.db',
  logDirectory: 'logs',
  isElevated: false,
  maxParallelScans: 4,
  maxBatchHosts: 50,
  machineName: 'LOCAL-PC',
  machineFqdn: 'LOCAL-PC.corp.example',
  runtimeProfile: 'Test',
};

function hardware(host: string): HardwareInfoResult {
  return {
    host,
    capturedAtUtc: '2026-08-18T08:00:00Z',
    fromCache: true,
    snapshot: {
      cpu: { name: 'Test CPU', physicalCores: 4, logicalProcessors: 8, maxClockSpeedMhz: 3000 },
      memoryBanks: [{ manufacturer: null, partNumber: null, capacityBytes: 8 * 1024 ** 3, speedMtps: null }],
      disks: [{ model: 'Test SSD', sizeBytes: 256 * 1024 ** 3, interfaceType: 'NVMe', mediaType: 'SSD' }],
      operatingSystem: {
        caption: 'Windows 11 Pro',
        version: '10.0.26100',
        buildNumber: '26100',
        architecture: '64-bit',
      },
      networkAdapters: null,
      gpus: null,
      monitors: null,
      installedSoftware: [],
      installedSoftwareError: null,
      userEvidence: null,
    },
  };
}

function bridgeFailure(message: string) {
  return new BridgeInvokeError({
    code: 'INTERNAL_ERROR',
    message,
    details: 'simulated comparison backend failure',
  });
}

function installDefaultBridge() {
  invokeMock.mockImplementation((module: string, action: string, payload?: Record<string, unknown>) => {
    if (module === 'targets' && action === 'list') return Promise.resolve({ targets: [] });
    if (module === 'activedirectory' && action === 'searchComputers') return Promise.resolve(directory);
    if (module === 'inventory' && action === 'listHosts') return Promise.resolve(inventoryHosts);
    if (module === 'security' && action === 'listHosts') return Promise.resolve(securityHosts);
    if (module === 'system' && action === 'getAppInfo') return Promise.resolve(appInfo);
    if (module === 'inventory' && action === 'getHardwareInfo') {
      const target = payload?.target as { host?: string } | null;
      return Promise.resolve(hardware(target?.host ?? appInfo.machineName));
    }
    if (module === 'security' && action === 'getLatestScan') return Promise.resolve({ scan: null });
    return Promise.reject(new Error(`Unexpected action ${module}/${action}`));
  });
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/clients/compare']}>
      <TargetProvider>
        <Routes>
          <Route path="/clients/compare" element={<ComparePage />} />
        </Routes>
      </TargetProvider>
    </MemoryRouter>,
  );
}

async function chooseClients() {
  await chooseClient('First client', 'PC-A');
  await chooseClient('Second client', 'PC-B');
}

async function chooseClient(label: string, query: string) {
  const picker = await screen.findByRole('combobox', { name: label });
  await waitFor(() => expect((picker as HTMLInputElement).disabled).toBe(false));
  await userEvent.click(picker);
  await userEvent.clear(picker);
  await userEvent.type(picker, query);
  await userEvent.keyboard('{ArrowDown}{Enter}');
}

describe('ComparePage data truthfulness', () => {
  beforeEach(() => {
    localStorage.clear();
    invokeMock.mockReset();
    installDefaultBridge();
  });

  it('searches client choices and exposes known inventory availability before comparison', async () => {
    renderPage();

    const picker = await screen.findByRole('combobox', { name: 'First client' });
    expect(picker.tagName).toBe('INPUT');

    await userEvent.click(picker);
    await userEvent.type(picker, 'PC-B');

    const matches = screen.getByRole('listbox', { name: 'First client matches' });
    expect(within(matches).getByRole('option', { name: /PC-B.*Inventory.*Available.*Security.*Available/i })).toBeTruthy();
    expect(within(matches).queryByRole('option', { name: /PC-A/i })).toBeNull();

    await userEvent.keyboard('{ArrowDown}{Enter}');
    expect((picker as HTMLInputElement).value).toBe('PC-B');

    await userEvent.clear(picker);
    await userEvent.type(picker, 'PC-C');
    const missingInventoryChoice = within(screen.getByRole('listbox', { name: 'First client matches' }))
      .getByRole('option', { name: /PC-C.*Inventory.*Missing.*Security.*Missing/i });
    expect(missingInventoryChoice).toBeTruthy();
    await userEvent.click(missingInventoryChoice);
    expect((picker as HTMLInputElement).value).toBe('PC-C');
  });

  it('limits broad client-search results and reports the complete match count', async () => {
    const original = invokeMock.getMockImplementation();
    const manyComputers = Array.from({ length: 75 }, (_, index) => ({
      ...directory.computers[0],
      name: `BRANCH-${String(index).padStart(3, '0')}`,
      dnsHostName: `branch-${String(index).padStart(3, '0')}.corp.local`,
      distinguishedName: `CN=BRANCH-${String(index).padStart(3, '0')},DC=corp,DC=local`,
    }));
    invokeMock.mockImplementation((module: string, action: string, payload?: Record<string, unknown>) =>
      action === 'searchComputers'
        ? Promise.resolve({ ...directory, computers: manyComputers })
        : original?.(module, action, payload));

    renderPage();
    const picker = await screen.findByRole('combobox', { name: 'First client' });
    expect(picker.tagName).toBe('INPUT');
    await userEvent.click(picker);
    await userEvent.type(picker, 'BRANCH-');

    const matches = screen.getByRole('listbox', { name: 'First client matches' });
    expect(within(matches).getAllByRole('option')).toHaveLength(50);
    expect(within(matches).getByText('Showing 50 of 75 matches')).toBeTruthy();
  });

  it('persists completed comparisons and prioritizes available recent clients on the next visit', async () => {
    const firstVisit = renderPage();
    await chooseClients();
    await userEvent.click(screen.getByRole('button', { name: 'Compare' }));

    expect(await screen.findByText('Hardware inventory')).toBeTruthy();
    expect(JSON.parse(localStorage.getItem('wec.view.client-compare-recent') ?? 'null')).toEqual({
      version: 2,
      hosts: ['pc-a.corp.local', 'pc-b.corp.local'],
    });

    firstVisit.unmount();
    renderPage();
    const picker = await screen.findByRole('combobox', { name: 'First client' });
    await waitFor(() => expect((picker as HTMLInputElement).disabled).toBe(false));
    await userEvent.click(picker);

    const recentGroup = screen.getByRole('group', { name: 'Recently compared' });
    expect(within(recentGroup).getAllByRole('option').map((option) => option.textContent)).toEqual([
      expect.stringMatching(/^PC-A/),
      expect.stringMatching(/^PC-B/),
    ]);

    await userEvent.keyboard('{ArrowDown}{Enter}');
    expect((picker as HTMLInputElement).value).toBe('PC-A');
  });

  it('hides unavailable and duplicate recent hosts without changing explicit search results', async () => {
    localStorage.setItem('wec.view.client-compare-recent', JSON.stringify({
      version: 2,
      hosts: ['retired.corp.local', 'pc-c.corp.local', 'PC-C', 'pc-a.corp.local'],
    }));

    renderPage();
    const picker = await screen.findByRole('combobox', { name: 'First client' });
    await waitFor(() => expect((picker as HTMLInputElement).disabled).toBe(false));
    await userEvent.click(picker);

    const recentGroup = screen.getByRole('group', { name: 'Recently compared' });
    expect(within(recentGroup).getAllByRole('option').map((option) => option.textContent)).toEqual([
      expect.stringMatching(/^PC-C/),
      expect.stringMatching(/^PC-A/),
    ]);
    expect(within(recentGroup).queryByText(/retired/i)).toBeNull();

    await userEvent.type(picker, 'PC-B');
    expect(screen.queryByRole('group', { name: 'Recently compared' })).toBeNull();
    expect(within(screen.getByRole('listbox', { name: 'First client matches' })).getAllByRole('option'))
      .toHaveLength(1);
  });

  it.each([
    ['directory', 'activedirectory', 'searchComputers', 'The Active Directory client list could not be loaded.'],
    ['inventory', 'inventory', 'listHosts', 'The stored client inventory list could not be loaded.'],
    ['security', 'security', 'listHosts', 'The stored client security list could not be loaded.'],
  ])('keeps a failed %s selection source visible and safely retryable', async (
    _label,
    failedModule,
    failedAction,
    message,
  ) => {
    const original = invokeMock.getMockImplementation();
    invokeMock.mockImplementation((module: string, action: string, payload?: Record<string, unknown>) =>
      module === failedModule && action === failedAction
        ? Promise.reject(bridgeFailure(`${failedAction} failed`))
        : original?.(module, action, payload));

    renderPage();

    expect(await screen.findByText(message)).toBeTruthy();
    expect(screen.getByText('An unexpected internal error occurred.')).toBeTruthy();
    const technicalDetails = screen.getByText('Technical details').closest('details');
    expect(technicalDetails?.hasAttribute('open')).toBe(false);

    await userEvent.click(screen.getByRole('button', { name: 'Reload client sources' }));
    await waitFor(() => expect(
      invokeMock.mock.calls.filter((call) => call[1] === failedAction),
    ).toHaveLength(failedAction === 'listHosts' ? 4 : 2));
  });

  it('blocks comparison while the local machine identity is unverified and recovers on retry', async () => {
    const original = invokeMock.getMockImplementation();
    let appInfoCalls = 0;
    invokeMock.mockImplementation((module: string, action: string, payload?: Record<string, unknown>) => {
      if (action === 'getAppInfo') {
        appInfoCalls += 1;
        return appInfoCalls === 1
          ? Promise.reject(bridgeFailure('machine identity failed'))
          : Promise.resolve(appInfo);
      }
      return original?.(module, action, payload);
    });

    renderPage();
    await chooseClients();

    expect(await screen.findByText('The local machine identity could not be verified.')).toBeTruthy();
    expect((screen.getByRole('button', { name: 'Compare' }) as HTMLButtonElement).disabled).toBe(true);

    await userEvent.click(screen.getByRole('button', { name: 'Reload client sources' }));
    await waitFor(() => expect(screen.queryByText('The local machine identity could not be verified.')).toBeNull());
    expect((screen.getByRole('button', { name: 'Compare' }) as HTMLButtonElement).disabled).toBe(false);
  });

  it('does not claim that inventory is missing when a stored snapshot read fails', async () => {
    const original = invokeMock.getMockImplementation();
    invokeMock.mockImplementation((module: string, action: string, payload?: Record<string, unknown>) => {
      const target = payload?.target as { host?: string } | null;
      if (action === 'getHardwareInfo' && target?.host === 'pc-a.corp.local') {
        return Promise.reject(bridgeFailure('inventory snapshot failed'));
      }
      return original?.(module, action, payload);
    });

    renderPage();
    await chooseClients();
    await userEvent.click(screen.getByRole('button', { name: 'Compare' }));

    expect(await screen.findByText('Stored hardware inventory for pc-a.corp.local could not be read.')).toBeTruthy();
    expect(screen.queryByText(/pc-a\.corp\.local has no stored inventory snapshot/i)).toBeNull();
    expect(screen.getByText(/Code: INTERNAL_ERROR/)).toBeTruthy();

    await userEvent.click(screen.getByRole('button', { name: 'Retry comparison' }));
    await waitFor(() => expect(
      invokeMock.mock.calls.filter((call) => call[1] === 'getHardwareInfo'),
    ).toHaveLength(4));
  });

  it('keeps a typed missing inventory snapshot distinct from a technical failure', async () => {
    const original = invokeMock.getMockImplementation();
    invokeMock.mockImplementation((module: string, action: string, payload?: Record<string, unknown>) => {
      const target = payload?.target as { host?: string } | null;
      if (action === 'getHardwareInfo' && target?.host === 'pc-a.corp.local') {
        return Promise.reject(new BridgeInvokeError({
          code: 'NOT_FOUND',
          message: "No stored snapshot for 'pc-a.corp.local'.",
          details: null,
        }));
      }
      return original?.(module, action, payload);
    });

    renderPage();
    await chooseClients();
    await userEvent.click(screen.getByRole('button', { name: 'Compare' }));

    expect(await screen.findByText(/pc-a\.corp\.local has no stored inventory snapshot/i)).toBeTruthy();
    expect(screen.queryByText(/Inventory unavailable — pc-a\.corp\.local/i)).toBeNull();
    expect(screen.queryByRole('button', { name: 'Retry comparison' })).toBeNull();
  });

  it('does not claim that a security scan is missing when its read fails', async () => {
    const original = invokeMock.getMockImplementation();
    invokeMock.mockImplementation((module: string, action: string, payload?: Record<string, unknown>) => {
      const target = payload?.target as { host?: string } | null;
      if (action === 'getLatestScan' && target?.host === 'pc-b.corp.local') {
        return Promise.reject(bridgeFailure('security snapshot failed'));
      }
      return original?.(module, action, payload);
    });

    renderPage();
    await chooseClients();
    await userEvent.click(screen.getByRole('button', { name: 'Compare' }));

    expect(await screen.findByText('Stored security scan for pc-b.corp.local could not be read.')).toBeTruthy();
    expect(screen.queryByText(/pc-b\.corp\.local has no stored security scan/i)).toBeNull();
    expect(screen.getByRole('button', { name: 'Retry comparison' })).toBeTruthy();
  });

  it('discards a completed comparison when either selected client changes', async () => {
    renderPage();
    await chooseClients();
    await userEvent.click(screen.getByRole('button', { name: 'Compare' }));

    expect(await screen.findByText('Hardware inventory')).toBeTruthy();

    await chooseClient('First client', 'PC-B');

    expect(screen.getByText('Pick two clients')).toBeTruthy();
    expect(screen.queryByText('Hardware inventory')).toBeNull();

    await chooseClient('Second client', 'PC-A');
    expect((screen.getByRole('button', { name: 'Compare' }) as HTMLButtonElement).disabled).toBe(false);
    expect(screen.queryByText('Hardware inventory')).toBeNull();
  });

  it('keeps both client selections locked while their comparison is in flight', async () => {
    const original = invokeMock.getMockImplementation();
    let releaseInventory: ((value: HardwareInfoResult) => void) | undefined;
    const pendingInventory = new Promise<HardwareInfoResult>((resolve) => {
      releaseInventory = resolve;
    });
    invokeMock.mockImplementation((module: string, action: string, payload?: Record<string, unknown>) => {
      const target = payload?.target as { host?: string } | null;
      if (action === 'getHardwareInfo' && target?.host === 'pc-a.corp.local') {
        return pendingInventory;
      }
      return original?.(module, action, payload);
    });

    renderPage();
    await chooseClients();
    await userEvent.click(screen.getByRole('button', { name: 'Compare' }));

    expect((screen.getByLabelText('First client') as HTMLInputElement).disabled).toBe(true);
    expect((screen.getByLabelText('Second client') as HTMLInputElement).disabled).toBe(true);

    releaseInventory?.(hardware('pc-a.corp.local'));
    await waitFor(() => expect(screen.getByText('Hardware inventory')).toBeTruthy());
    expect((screen.getByLabelText('First client') as HTMLInputElement).disabled).toBe(false);
    expect((screen.getByLabelText('Second client') as HTMLInputElement).disabled).toBe(false);
  });
});
