import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type {
  OpsiConnectionStatusResult,
  PatchDashboardResult,
  RolloutPreview,
} from '../../shared/api-types';
import {
  formatClientName,
  normalizeCachedPatchView,
  PatchManagementPage,
  resolveDefaultDepot,
} from './PatchManagementPage';

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
  defaultDepotFilter: '',
};

const disconnectedStatus: OpsiConnectionStatusResult = {
  connected: false,
  serverUrl: null,
  userName: null,
  opsiVersion: null,
  defaultDepotFilter: '',
};

const dashboard: PatchDashboardResult = {
  serverUrl: 'https://opsi.kauth.local:4447/',
  depotFilter: null,
  generatedAtUtc: '2026-07-03T12:00:00Z',
  summary: {
    productCount: 1,
    productsWithUpdates: 1,
    productsWithDepotDeviation: 0,
    productsMissingOnDepots: 0,
    productsWithFailures: 0,
    pendingRolloutCount: 0,
    outdatedClientCount: 1,
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
      referenceVersion: '128.0-2',
      manufacturerVersion: '129.0',
      manufacturerCheckStatus: 'SUCCESS',
      manufacturerCheckedAtUtc: '2026-07-03T11:55:00Z',
      manufacturerCheckError: null,
      manufacturerUpdateAvailable: true,
      depotVersions: [{ depotId: 'depot-denkingen.kauth.local', version: '128.0-2' }],
      missingDepotIds: [],
      packageStatus: 'UPDATE_AVAILABLE',
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
      case 'listVersionSources':
        return Promise.resolve({ sources: [] });
      case 'saveVersionSource':
        return Promise.resolve({
          sources: [{
            productId: 'firefox',
            sourceUrl: 'https://vendor.example/releases',
            versionPattern: 'Version ([0-9.]+)',
            enabled: true,
            latestVersion: null,
            lastCheckedUtc: null,
            checkStatus: 'NOT_CHECKED',
            lastError: null,
          }],
        });
      case 'checkVendorVersions':
        return Promise.resolve({ outcomes: [] });
      case 'getRolloutPreview':
        return Promise.resolve(preview);
      case 'requestRollout':
        return Promise.resolve({ requestedClientCount: 1 });
      case 'getPackageWorkflowStatus':
        return Promise.resolve({
          productId: 'firefox',
          testDepotId: null,
          testedVersion: null,
          testUpdateSucceededAtUtc: null,
          pilotApprovedAtUtc: null,
          pilotApproved: false,
          lastSynchronizationResult: null,
          lastSynchronizationAtUtc: null,
          lastError: null,
        });
      case 'preparePackages':
        return Promise.resolve({
          productId: 'firefox',
          stage: 'TEST',
          mode: 'REPOSITORY',
          artifactVersion: null,
          targets: [{
            depotId: 'depot-denkingen.kauth.local',
            host: 'depot-denkingen.kauth.local',
            currentVersion: '128.0-2',
            command: 'opsi-package-updater -v update firefox',
          }],
          note: 'Test depot only.',
          confirmationText: "I want to update 'firefox' on the test depot.",
          generatedAtUtc: '2026-07-03T12:02:00Z',
        });
      case 'executePackageUpdate':
        return Promise.resolve({
          productId: 'firefox',
          stage: 'TEST',
          succeededTargetCount: 1,
          failedTargetCount: 0,
          targets: [{
            depotId: 'depot-denkingen.kauth.local',
            success: true,
            exitCode: 0,
            oldVersion: '128.0-2',
            newVersion: '129.0-1',
            error: null,
          }],
        });
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

describe('formatClientName', () => {
  it('removes only a trailing kauth.local DNS suffix, case-insensitively', () => {
    expect(formatClientName('pc1.kauth.local')).toBe('pc1');
    expect(formatClientName('PC2.KAUTH.LOCAL')).toBe('PC2');
    expect(formatClientName('pc3.example.local')).toBe('pc3.example.local');
    expect(formatClientName('pc4.kauth.local.example')).toBe('pc4.kauth.local.example');
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
    expect(screen.getByText(/Gespeicherte Ansicht vom/)).toBeDefined();
    // … and nothing may act on them or silently re-query opsi.
    expect(screen.getByRole('button', { name: 'Übersicht aktualisieren' }).hasAttribute('disabled')).toBe(true);
    expect(dashboardCalls()).toEqual([]);
    // Connection management is centralised in Settings; Patch Management only links there.
    expect(screen.getByRole('link', { name: 'Zu den Einstellungen' }).getAttribute('href')).toBe('#/settings');
  });

  it('migrates the legacy cached dashboard instead of crashing on missing array fields', async () => {
    const legacyDashboard = JSON.parse(JSON.stringify(dashboard)) as {
      summary: Record<string, unknown>;
      products: Array<Record<string, unknown>>;
    };
    delete legacyDashboard.summary.productsWithDepotDeviation;
    delete legacyDashboard.summary.productsMissingOnDepots;
    delete legacyDashboard.summary.outdatedClientCount;
    for (const product of legacyDashboard.products) {
      delete product.referenceVersion;
      delete product.manufacturerVersion;
      delete product.manufacturerCheckStatus;
      delete product.manufacturerCheckedAtUtc;
      delete product.manufacturerCheckError;
      delete product.manufacturerUpdateAvailable;
      delete product.missingDepotIds;
      delete product.packageStatus;
    }
    const legacyView = {
      server: 'opsi.kauth.local',
      userName: 'admin',
      depotFilter: '',
      dashboard: legacyDashboard,
    };
    localStorage.setItem('wec.view.patchmanagement', JSON.stringify(legacyView));
    invokeMock.mockImplementation((_module: string, action: string) =>
      action === 'getConnectionStatus'
        ? Promise.resolve(disconnectedStatus)
        : Promise.resolve({ mappings: [], entries: [], sources: [] }),
    );

    expect(normalizeCachedPatchView(legacyView)?.dashboard?.products[0].missingDepotIds)
      .toEqual(['opsi.kauth.local']);
    render(<PatchManagementPage />);

    expect(await screen.findByText('Mozilla Firefox')).toBeDefined();
    expect(screen.queryByText('THIS PAGE CRASHED')).toBeNull();
    expect(screen.getAllByText('Paket fehlt auf Depot').length).toBeGreaterThan(0);
  });

  it('does not expose credentials in Patch Management or write them into its cached view', async () => {
    invokeMock.mockImplementation((_module: string, action: string) =>
      action === 'getConnectionStatus'
        ? Promise.resolve(disconnectedStatus)
        : Promise.resolve({ mappings: [], entries: [] }),
    );

    render(<PatchManagementPage />);

    await waitFor(() => expect(localStorage.getItem('wec.view.patchmanagement')).not.toBeNull());
    expect(screen.queryByLabelText('Passwort')).toBeNull();
    expect(localStorage.getItem('wec.view.patchmanagement')).not.toContain('password');
  });

  it('links to central opsi settings when disconnected', async () => {
    invokeMock.mockImplementation((_module: string, action: string) =>
      action === 'getConnectionStatus'
        ? Promise.resolve(disconnectedStatus)
        : Promise.reject(new Error(`Unexpected action ${action}`)),
    );

    render(<PatchManagementPage />);

    expect(await screen.findByText(/verbindet sich hier automatisch/)).toBeDefined();
    expect(screen.getByRole('link', { name: 'Zu den Einstellungen' }).getAttribute('href')).toBe('#/settings');
    expect(screen.getByText('Nicht verbunden')).toBeDefined();
  });

  it('loads the central dashboard across all depots by default', async () => {
    mockConnectedBridge();

    render(<PatchManagementPage />);

    // 'firefox' shows up in the product list and the audit history
    expect((await screen.findAllByText('firefox')).length).toBeGreaterThan(0);
    await waitFor(() => expect(dashboardCalls()).toContainEqual({ depotFilter: null }));
    const select = screen.getByRole('combobox', { name: 'Depotfilter' }) as HTMLSelectElement;
    expect(select.value).toBe('');
    expect(screen.getByText('Mozilla Firefox')).toBeDefined();
    expect(screen.getAllByText('Update verfügbar').length).toBeGreaterThan(0);
  });

  it('shows the unique client count and short client names', async () => {
    mockConnectedBridge();

    render(<PatchManagementPage />);

    const clientsTab = await screen.findByRole('tab', { name: 'Clients (2)' });
    await userEvent.click(clientsTab);

    expect(screen.getByText('2 eindeutige Clients · 1 Paketstand')).toBeDefined();
    const clientName = screen.getByText('pc1');
    expect(clientName.getAttribute('title')).toBe('pc1.kauth.local');
    expect(screen.queryByText('pc1.kauth.local')).toBeNull();
  });

  it('requires preview and explicit confirmation before requesting a rollout', async () => {
    mockConnectedBridge();

    render(<PatchManagementPage />);
    await userEvent.click(await screen.findByRole('button', { name: /Mozilla Firefox/ }));
    await userEvent.click(screen.getByRole('button', { name: /Deployment vorbereiten/ }));

    // Preview shown, nothing sent yet
    expect(await screen.findByText(/Es wurde noch nichts an opsi übermittelt/)).toBeDefined();
    const rolloutButton = screen.getByRole('button', { name: /Deployment für 1 Client/ });
    expect(rolloutButton.hasAttribute('disabled')).toBe(true);
    expect(invokeMock.mock.calls.some((call) => call[1] === 'requestRollout')).toBe(false);

    await userEvent.click(screen.getByRole('checkbox', { name: /Ich habe die betroffenen Clients geprüft/ }));
    await userEvent.click(rolloutButton);

    await waitFor(() =>
      expect(
        invokeMock.mock.calls.find((call) => call[1] === 'requestRollout')?.[2],
      ).toMatchObject({ productId: 'firefox', confirmed: true, clientIds: ['pc1.kauth.local'] }),
    );
    expect(await screen.findByText(/Deployment für 1 Client/)).toBeDefined();
  });

  it('requires an SSH preview and explicit confirmation for a test-depot update', async () => {
    mockConnectedBridge();

    render(<PatchManagementPage />);
    await userEvent.click(await screen.findByRole('button', { name: /Mozilla Firefox/ }));
    await userEvent.click(screen.getByRole('button', { name: 'Testupdate vorbereiten' }));

    expect(await screen.findByText('opsi-package-updater -v update firefox')).toBeDefined();
    const executeButton = screen.getByRole('button', { name: 'Bestätigen und über SSH ausführen' });
    expect(executeButton.hasAttribute('disabled')).toBe(true);
    expect(invokeMock.mock.calls.some((call) => call[1] === 'executePackageUpdate')).toBe(false);

    await userEvent.click(screen.getByRole('checkbox', { name: /I want to update 'firefox'/ }));
    await userEvent.click(executeButton);

    await waitFor(() => expect(
      invokeMock.mock.calls.find((call) => call[1] === 'executePackageUpdate')?.[2],
    ).toMatchObject({
      productId: 'firefox',
      stage: 'TEST',
      depotIds: ['depot-denkingen.kauth.local'],
      confirmed: true,
    }));
    expect(await screen.findByText(/1 erfolgreich/)).toBeDefined();
  });

  it('only offers depot synchronization after pilot approval and excludes the test depot', async () => {
    mockConnectedBridge();
    const original = invokeMock.getMockImplementation();
    invokeMock.mockImplementation((module: string, action: string, payload: unknown) => {
      if (action === 'getPackageWorkflowStatus') {
        return Promise.resolve({
          productId: 'firefox',
          testDepotId: 'depot-denkingen.kauth.local',
          testedVersion: '129.0-1',
          testUpdateSucceededAtUtc: '2026-07-03T12:03:00Z',
          pilotApprovedAtUtc: '2026-07-03T12:10:00Z',
          pilotApproved: true,
          lastSynchronizationResult: null,
          lastSynchronizationAtUtc: null,
          lastError: null,
        });
      }
      if (action === 'preparePackages') {
        return Promise.resolve({
          productId: 'firefox',
          stage: 'DEPOT_SYNC',
          mode: 'REPOSITORY',
          artifactVersion: null,
          targets: [{
            depotId: 'opsi.kauth.local',
            host: 'opsi.kauth.local',
            currentVersion: '128.0-2',
            command: 'opsi-package-updater -v update firefox',
          }],
          note: 'Synchronize remaining depots.',
          confirmationText: 'Synchronize one depot.',
          generatedAtUtc: '2026-07-03T12:11:00Z',
        });
      }
      return original!(module, action, payload);
    });

    render(<PatchManagementPage />);
    await userEvent.click(await screen.findByRole('button', { name: /Mozilla Firefox/ }));
    const syncButton = await screen.findByRole('button', { name: 'Auf weitere Depots verteilen' });
    await waitFor(() => expect(syncButton.hasAttribute('disabled')).toBe(false));
    await userEvent.click(syncButton);

    await waitFor(() => expect(
      invokeMock.mock.calls.find((call) => call[1] === 'preparePackages')?.[2],
    ).toMatchObject({
      productId: 'firefox',
      stage: 'DEPOT_SYNC',
      depotIds: ['opsi.kauth.local'],
    }));
  });

  it('renders the audit history', async () => {
    mockConnectedBridge();

    render(<PatchManagementPage />);

    await userEvent.click(await screen.findByRole('tab', { name: 'Historie' }));
    expect(await screen.findByText('ROLLOUT_REQUESTED')).toBeDefined();
    expect(screen.getByText('vinz')).toBeDefined();
    expect(screen.getByText('SUCCESS')).toBeDefined();
  });

  it('offers mapping inputs for unmapped inventory software', async () => {
    mockConnectedBridge();

    render(<PatchManagementPage />);

    await userEvent.click(await screen.findByRole('tab', { name: 'Zuordnungen' }));
    expect(await screen.findByText('Notepad++')).toBeDefined();
    const input = screen.getByLabelText('opsi product id for Notepad++');
    expect((input as HTMLInputElement).value).toBe('');
    // Without a product id the map action stays disabled
    expect(screen.getByRole('button', { name: 'Zuordnen' }).hasAttribute('disabled')).toBe(true);
  });

  it('configures an auditable manufacturer version source', async () => {
    mockConnectedBridge();
    render(<PatchManagementPage />);

    await userEvent.click(await screen.findByRole('tab', { name: 'Automatisierung' }));
    await userEvent.selectOptions(
      screen.getByRole('combobox', { name: 'opsi-Produkt für Herstellerquelle' }),
      'firefox',
    );
    await userEvent.type(screen.getByLabelText('HTTPS-URL der Herstellerquelle'), 'https://vendor.example/releases');
    await userEvent.type(screen.getByLabelText('Versionsmuster'), 'Version (1.2.3)');
    await userEvent.click(screen.getByRole('button', { name: 'Quelle speichern' }));

    await waitFor(() => expect(
      invokeMock.mock.calls.find((call) => call[1] === 'saveVersionSource')?.[2],
    ).toMatchObject({ productId: 'firefox', sourceUrl: 'https://vendor.example/releases' }));
    expect(await screen.findByText('NOT_CHECKED')).toBeDefined();
  });
});
