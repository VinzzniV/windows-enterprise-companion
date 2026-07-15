import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type {
  OpsiConnectionStatusResult,
  PatchDashboardResult,
  RolloutPreview,
} from '../../shared/api-types';
import { PatchManagementPage, resolveDefaultDepot } from './PatchManagementPage';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../shared/bridge/bridgeClient', () => ({
  invoke: invokeMock,
  BridgeInvokeError: class extends Error {},
}));

const connectedStatus: OpsiConnectionStatusResult = {
  connected: true,
  serverUrl: 'https://opsi.kauth.local:4447/',
  userName: 'admin',
  opsiVersion: '4.3.1.2',
  defaultDepotFilter: 'Denkingen',
};

const disconnectedStatus: OpsiConnectionStatusResult = {
  connected: false,
  serverUrl: null,
  userName: null,
  opsiVersion: null,
  defaultDepotFilter: 'Denkingen',
};

const dashboard: PatchDashboardResult = {
  serverUrl: 'https://opsi.kauth.local:4447/',
  depotFilter: null,
  generatedAtUtc: '2026-07-03T12:00:00Z',
  summary: {
    productCount: 1,
    productsWithUpdates: 1,
    productsWithFailures: 0,
    pendingRolloutCount: 0,
    clientCount: 2,
    depotCount: 2,
    unmappedSoftwareCount: 1,
  },
  depots: [
    { id: 'opsi.kauth.local', description: 'Main server', isConfigServer: true, clientCount: 0 },
    {
      id: 'depot-denkingen.kauth.local',
      description: 'Denkingen',
      isConfigServer: false,
      clientCount: 2,
    },
  ],
  products: [
    {
      productId: 'firefox',
      name: 'Mozilla Firefox',
      availableVersion: '128.0-2',
      depotVersions: [{ depotId: 'depot-denkingen.kauth.local', version: '128.0-2' }],
      state: 'UPDATE_AVAILABLE',
      installedClientCount: 2,
      outdatedClientCount: 1,
      failedClientCount: 0,
      pendingActionCount: 0,
      lastError: null,
      clients: [
        {
          clientId: 'pc1.kauth.local',
          depotId: 'depot-denkingen.kauth.local',
          installedVersion: '127.0-1',
          targetVersion: '128.0-2',
          installationStatus: 'installed',
          actionRequest: 'none',
          actionResult: 'successful',
          state: 'UPDATE_AVAILABLE',
        },
      ],
      mappedSoftwareNames: ['Mozilla Firefox'],
      inventoryDetections: [{ host: 'HOST-A', version: '127.0' }],
    },
  ],
  unmappedSoftware: [
    { name: 'Notepad++', versions: ['8.6'], hostCount: 1, suggestedProductId: null },
  ],
};

const preview: RolloutPreview = {
  productId: 'firefox',
  productName: 'Mozilla Firefox',
  depotFilter: 'depot-denkingen.kauth.local',
  plannedAction: 'setup',
  clients: [
    {
      clientId: 'pc1.kauth.local',
      depotId: 'depot-denkingen.kauth.local',
      installedVersion: '127.0-1',
      targetVersion: '128.0-2',
      currentState: 'UPDATE_AVAILABLE',
    },
  ],
  generatedAtUtc: '2026-07-03T12:01:00Z',
};

function mockConnectedBridge() {
  invokeMock.mockImplementation((_module: string, action: string) => {
    switch (action) {
      case 'getConnectionStatus':
        return Promise.resolve(connectedStatus);
      case 'getDashboard':
        return Promise.resolve(dashboard);
      case 'listMappings':
        return Promise.resolve({ mappings: [] });
      case 'getAuditLog':
        return Promise.resolve({
          entries: [
            {
              id: 1,
              timestampUtc: '2026-07-03T11:00:00Z',
              userName: 'vinz',
              action: 'ROLLOUT_REQUESTED',
              productId: 'firefox',
              depotId: 'depot-denkingen.kauth.local',
              targetClients: ['pc1.kauth.local'],
              previewJson: null,
              result: 'SUCCESS',
              errorMessage: null,
            },
          ],
        });
      case 'getRolloutPreview':
        return Promise.resolve(preview);
      case 'requestRollout':
        return Promise.resolve({ requestedClientCount: 1 });
      default:
        return Promise.reject(new Error(`Unexpected action ${action}`));
    }
  });
}

function dashboardCalls(): unknown[] {
  return invokeMock.mock.calls
    .filter((call) => call[1] === 'getDashboard')
    .map((call) => call[2]);
}

describe('resolveDefaultDepot', () => {
  it('matches by id or description, case-insensitively', () => {
    expect(
      resolveDefaultDepot(
        [
          { id: 'opsi.kauth.local', description: 'Main server' },
          { id: 'depot-denkingen.kauth.local', description: null },
        ],
        'Denkingen',
      ),
    ).toBe('depot-denkingen.kauth.local');
    expect(
      resolveDefaultDepot([{ id: 'depot-x.local', description: 'Standort Denkingen' }], 'denkingen'),
    ).toBe('depot-x.local');
    expect(resolveDefaultDepot([{ id: 'depot-x.local', description: null }], 'Rottweil')).toBeNull();
    expect(resolveDefaultDepot([{ id: 'depot-x.local', description: null }], '')).toBeNull();
  });
});

describe('PatchManagementPage', () => {
  beforeEach(() => {
    invokeMock.mockReset();
    localStorage.clear();
  });

  it('restores the cached dashboard while disconnected and keeps it read-only', async () => {
    localStorage.setItem(
      'wec.view.patchmanagement',
      JSON.stringify({
        server: 'opsi.kauth.local',
        userName: 'admin',
        depotFilter: '',
        dashboard,
      }),
    );
    invokeMock.mockImplementation((_module: string, action: string) =>
      action === 'getConnectionStatus'
        ? Promise.resolve(disconnectedStatus)
        : Promise.resolve({ mappings: [], entries: [] }),
    );

    render(<PatchManagementPage />);

    // The stored products are on screen even though there is no opsi session …
    expect(await screen.findByText('Mozilla Firefox')).toBeDefined();
    expect(screen.getByText(/Stored view from/)).toBeDefined();
    // … and nothing may act on them or silently re-query opsi.
    expect(screen.getByRole('button', { name: 'Refresh' }).hasAttribute('disabled')).toBe(true);
    expect(dashboardCalls()).toEqual([]);
    // The server/user come back so only the password is missing
    expect((screen.getByLabelText('opsi server') as HTMLInputElement).value).toBe('opsi.kauth.local');
  });

  it('never writes the password into the cached view', async () => {
    invokeMock.mockImplementation((_module: string, action: string) =>
      action === 'getConnectionStatus'
        ? Promise.resolve(disconnectedStatus)
        : Promise.resolve({ mappings: [], entries: [] }),
    );

    render(<PatchManagementPage />);
    await userEvent.type(await screen.findByLabelText('Password'), 'hunter2');

    await waitFor(() => expect(localStorage.getItem('wec.view.patchmanagement')).not.toBeNull());
    expect(localStorage.getItem('wec.view.patchmanagement')).not.toContain('hunter2');
  });

  it('shows the connection form with the session-only credential note when disconnected', async () => {
    invokeMock.mockImplementation((_module: string, action: string) =>
      action === 'getConnectionStatus'
        ? Promise.resolve(disconnectedStatus)
        : Promise.reject(new Error(`Unexpected action ${action}`)),
    );

    render(<PatchManagementPage />);

    expect(await screen.findByText('opsi server')).toBeDefined();
    expect(screen.getByText(/kept in memory for this session only/)).toBeDefined();
    expect(screen.getByText('Not connected')).toBeDefined();
  });

  it('loads the dashboard and preselects the Denkingen depot', async () => {
    mockConnectedBridge();

    render(<PatchManagementPage />);

    // 'firefox' shows up in the product list and the audit history
    expect((await screen.findAllByText('firefox')).length).toBeGreaterThan(0);
    // First load without a filter to discover depots, then the default depot
    await waitFor(() =>
      expect(dashboardCalls()).toContainEqual({ depotFilter: 'depot-denkingen.kauth.local' }),
    );
    const select = screen.getByRole('combobox') as HTMLSelectElement;
    await waitFor(() => expect(select.value).toBe('depot-denkingen.kauth.local'));
    expect(screen.getByText('Mozilla Firefox')).toBeDefined();
    expect(screen.getByText('Update available')).toBeDefined();
  });

  it('requires preview and explicit confirmation before requesting a rollout', async () => {
    mockConnectedBridge();

    render(<PatchManagementPage />);
    await userEvent.click(await screen.findByRole('button', { name: /firefox/ }));
    await userEvent.click(screen.getByRole('button', { name: /Preview rollout/ }));

    // Preview shown, nothing sent yet
    expect(await screen.findByText(/Nothing has been sent to opsi yet/)).toBeDefined();
    const rolloutButton = screen.getByRole('button', { name: /Request rollout for 1 client/ });
    expect(rolloutButton.hasAttribute('disabled')).toBe(true);
    expect(invokeMock.mock.calls.some((call) => call[1] === 'requestRollout')).toBe(false);

    await userEvent.click(screen.getByRole('checkbox', { name: /I reviewed the affected clients/ }));
    await userEvent.click(rolloutButton);

    await waitFor(() =>
      expect(
        invokeMock.mock.calls.find((call) => call[1] === 'requestRollout')?.[2],
      ).toMatchObject({ productId: 'firefox', confirmed: true, clientIds: ['pc1.kauth.local'] }),
    );
    expect(await screen.findByText(/Rollout requested for 1 client/)).toBeDefined();
  });

  it('renders the audit history', async () => {
    mockConnectedBridge();

    render(<PatchManagementPage />);

    expect(await screen.findByText('ROLLOUT_REQUESTED')).toBeDefined();
    expect(screen.getByText('vinz')).toBeDefined();
    expect(screen.getByText('SUCCESS')).toBeDefined();
  });

  it('offers mapping inputs for unmapped inventory software', async () => {
    mockConnectedBridge();

    render(<PatchManagementPage />);

    expect(await screen.findByText('Notepad++')).toBeDefined();
    const input = screen.getByLabelText('opsi product id for Notepad++');
    expect((input as HTMLInputElement).value).toBe('');
    // Without a product id the map action stays disabled
    expect(screen.getByRole('button', { name: 'Map' }).hasAttribute('disabled')).toBe(true);
  });
});
