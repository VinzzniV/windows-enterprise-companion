import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type {
  OpsiConnectionStatusResult,
  PatchClientState,
  PatchDashboardOverview,
  RolloutPreview,
} from '../../shared/api-types';
import { BridgeInvokeError } from '../../shared/bridge/bridgeClient';
import { PatchManagementPage } from './PatchManagementPage';
import { formatClientName } from './PatchClientFleetCard';
import {
  normalizeCachedPatchView,
  resolveDefaultDepot,
} from './usePatchManagementWorkspace';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../shared/bridge/bridgeClient', async (importOriginal) => {
  const original = await importOriginal<typeof import('../../shared/bridge/bridgeClient')>();
  return { ...original, invoke: invokeMock };
});

const connectedStatus: OpsiConnectionStatusResult = {
  connected: true,
  serverUrl: 'https://opsi.kauth.local:4447/',
  userName: 'admin',
  opsiVersion: '4.3.1.2',
  defaultDepotFilter: '',
  connectionError: null,
};

const disconnectedStatus: OpsiConnectionStatusResult = {
  connected: false,
  serverUrl: null,
  userName: null,
  opsiVersion: null,
  defaultDepotFilter: '',
  connectionError: null,
};

const dashboard: PatchDashboardOverview = {
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
      mappedSoftwareNames: ['Mozilla Firefox'],
      inventoryDetections: [{ host: 'HOST-A', version: '127.0' }],
    },
  ],
  unmappedSoftware: [
    { name: 'Notepad++', versions: ['8.6'], hostCount: 1, suggestedProductId: null },
  ],
};

const patchClientState: PatchClientState = {
  clientId: 'pc1.kauth.local',
  depotId: 'depot-denkingen.kauth.local',
  installedVersion: '127.0-1',
  targetVersion: '128.0-2',
  installationStatus: 'installed',
  actionRequest: 'none',
  actionResult: 'successful',
  state: 'UPDATE_AVAILABLE',
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

function mockConnectedBridge(
  dashboardResult: PatchDashboardOverview = dashboard,
  clientStates: PatchClientState[] = [patchClientState],
) {
  invokeMock.mockImplementation((_module: string, action: string, payload?: Record<string, unknown>) => {
    switch (action) {
      case 'getConnectionStatus':
        return Promise.resolve(connectedStatus);
      case 'getDashboard':
        return Promise.resolve(dashboardResult);
      case 'listClientStates':
        {
          let items = clientStates.map((client) => ({
            productId: 'firefox',
            productName: 'Mozilla Firefox',
            client,
          }));
          const clientSearch = String(payload?.clientSearch ?? '').toLocaleLowerCase();
          const productSearch = String(payload?.productSearch ?? '').toLocaleLowerCase();
          if (clientSearch) {
            items = items.filter((item) => [
              item.client.clientId,
              item.client.depotId,
              item.client.installedVersion,
              item.client.targetVersion,
            ].some((value) => value?.toLocaleLowerCase().includes(clientSearch)));
          }
          if (productSearch && !'firefox mozilla firefox'.includes(productSearch)) items = [];
          if (payload?.state) items = items.filter((item) => item.client.state === payload.state);
          if (payload?.installationStatus) {
            items = items.filter((item) => item.client.installationStatus === payload.installationStatus);
          }
          items.sort((left, right) => left.client.clientId.localeCompare(right.client.clientId));
          if (payload?.sortDirection === 'desc') items.reverse();
          const page = Number(payload?.page ?? 1);
          const pageSize = Number(payload?.pageSize ?? 50);
          return Promise.resolve({
            items: items.slice((page - 1) * pageSize, page * pageSize),
            total: items.length,
            snapshotTotal: clientStates.length,
            page,
            pageSize,
          });
        }
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

function mockRejectedAction(actionName: string, error: Error): void {
  mockConnectedBridge();
  const defaultImplementation = invokeMock.getMockImplementation();
  invokeMock.mockImplementation(
    (module: string, action: string, payload?: Record<string, unknown>) => {
      if (action === actionName) return Promise.reject(error);
      return defaultImplementation?.(module, action, payload);
    },
  );
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
        dashboard: {
          ...dashboard,
          products: [{ ...dashboard.products[0], clients: [patchClientState] }],
        },
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
    expect(screen.getByText(/Saved view from/)).toBeDefined();
    // … and nothing may act on them or silently re-query opsi.
    expect(screen.getByRole('button', { name: 'Refresh overview' }).hasAttribute('disabled')).toBe(true);
    expect(dashboardCalls()).toEqual([]);
    await waitFor(() => expect(localStorage.getItem('wec.view.patchmanagement')).not.toContain('"clients":'));
    // Connection management is centralised in Settings; Patch Management only links there.
    expect(screen.getByRole('link', { name: 'Go to Settings' }).getAttribute('href')).toBe('#/settings');
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
    expect(screen.getAllByText('Missing').length).toBeGreaterThan(0);
    expect(screen.getAllByText('From depot').length).toBeGreaterThan(0);
  });

  it('does not expose credentials in Patch Management or write them into its cached view', async () => {
    invokeMock.mockImplementation((_module: string, action: string) =>
      action === 'getConnectionStatus'
        ? Promise.resolve(disconnectedStatus)
        : Promise.resolve({ mappings: [], entries: [] }),
    );

    render(<PatchManagementPage />);

    await waitFor(() => expect(localStorage.getItem('wec.view.patchmanagement')).not.toBeNull());
    expect(screen.queryByLabelText('Password')).toBeNull();
    expect(localStorage.getItem('wec.view.patchmanagement')).not.toContain('password');
  });

  it('links to central opsi settings when disconnected', async () => {
    invokeMock.mockImplementation((_module: string, action: string) =>
      action === 'getConnectionStatus'
        ? Promise.resolve(disconnectedStatus)
        : Promise.reject(new Error(`Unexpected action ${action}`)),
    );

    render(<PatchManagementPage />);

    expect(await screen.findByText(/connects automatically/)).toBeDefined();
    expect(screen.getByRole('link', { name: 'Go to Settings' }).getAttribute('href')).toBe('#/settings');
    expect(screen.getByText('Not connected')).toBeDefined();
  });

  it('shows the connection check as running before a disconnected result is verified', async () => {
    let resolveConnection!: (value: OpsiConnectionStatusResult) => void;
    const pendingConnection = new Promise<OpsiConnectionStatusResult>((resolve) => {
      resolveConnection = resolve;
    });
    invokeMock.mockImplementation((_module: string, action: string) => {
      if (action === 'getConnectionStatus') return pendingConnection;
      return Promise.resolve({ mappings: [], entries: [], sources: [] });
    });

    render(<PatchManagementPage />);

    const header = screen.getByRole('heading', { name: 'Patch Management' }).closest('header')!;
    expect(within(header).getByText('Running')).toBeDefined();
    expect(within(header).getByText('opsi connection')).toBeDefined();
    expect(screen.queryByText('Not connected')).toBeNull();
    expect(screen.queryByText(/connects automatically/)).toBeNull();

    resolveConnection(disconnectedStatus);
    expect(await within(header).findByText('Unknown')).toBeDefined();
    expect(within(header).getByText('Not connected')).toBeDefined();
    expect(await screen.findByText(/connects automatically/)).toBeDefined();
  });

  it('loads the central dashboard across all depots by default', async () => {
    mockConnectedBridge();

    render(<PatchManagementPage />);

    // 'firefox' shows up in the product list and the audit history
    expect((await screen.findAllByText('firefox')).length).toBeGreaterThan(0);
    await waitFor(() => expect(dashboardCalls()).toContainEqual({ depotFilter: null }));
    const select = screen.getByRole('combobox', { name: 'Depot filter' }) as HTMLSelectElement;
    expect(select.value).toBe('');
    const productRow = screen.getByText('Mozilla Firefox').closest('tr') as HTMLTableRowElement;
    expect(within(productRow).getByText('Update available').className).toContain('border-warn-700');

    await userEvent.click(screen.getByRole('tab', { name: 'Clients (2)' }));
    const clientRow = (await screen.findByText('pc1')).closest('tr') as HTMLTableRowElement;
    expect(within(clientRow).getByText('Update available').className).toContain('border-warn-700');
  });

  it('presents an incomplete client workflow as Pending with its concrete milestone', async () => {
    mockConnectedBridge(dashboard, [{ ...patchClientState, state: 'ROLLOUT_REQUESTED' }]);

    render(<PatchManagementPage />);

    await userEvent.click(await screen.findByRole('tab', { name: 'Clients (2)' }));
    const clientRow = (await screen.findByText('pc1')).closest('tr') as HTMLTableRowElement;
    expect(within(clientRow).getByText('Pending').className).toContain('border-warn-700');
    expect(within(clientRow).getByText('Deployment requested')).toBeDefined();
  });

  it('uses the English product language across every read-only workspace section', async () => {
    mockConnectedBridge();

    render(<PatchManagementPage />);

    expect(await screen.findByText(
      'Central overview of package versions, depot consistency, and controlled software rollouts.',
    )).toBeDefined();
    const header = screen.getByRole('heading', { name: 'Patch Management' }).closest('header')!;
    expect(within(header).getByText('Available')).toBeDefined();
    expect(within(header).getByText('opsi connection')).toBeDefined();
    expect(within(header).getByText(/https:\/\/opsi\.kauth\.local:4447\/ as admin/)).toBeDefined();
    expect(screen.getByRole('tab', { name: 'Package overview' })).toBeDefined();
    expect(screen.getByRole('tab', { name: 'History' })).toBeDefined();
    expect(screen.getByRole('tab', { name: 'Automation' })).toBeDefined();
    expect(screen.getByRole('tab', { name: 'Mappings' })).toBeDefined();
    expect(screen.getAllByText('Update available').length).toBeGreaterThan(0);

    await userEvent.click(screen.getByRole('button', { name: /Mozilla Firefox/ }));
    expect(await screen.findByText('Package details — Mozilla Firefox')).toBeDefined();
    expect(screen.getByText('Manufacturer version')).toBeDefined();
    expect(screen.getByRole('button', { name: /Prepare deployment/ })).toBeDefined();

    await userEvent.click(screen.getByRole('tab', { name: 'Clients (2)' }));
    expect(await screen.findByText('Client and deployment status')).toBeDefined();
    expect(screen.getByRole('searchbox', { name: 'Filter patch clients' })).toBeDefined();

    await userEvent.click(screen.getByRole('tab', { name: 'Automation' }));
    expect(screen.getByText('Scheduled checks')).toBeDefined();
    expect(screen.getByText('Controlled rollout')).toBeDefined();

    await userEvent.click(screen.getByRole('tab', { name: 'Mappings' }));
    expect(screen.getByText('Inventoried software without an opsi mapping')).toBeDefined();

    await userEvent.click(screen.getByRole('tab', { name: 'History' }));
    expect(screen.getByText('Package and deployment history')).toBeDefined();
  });

  it('mounts manufacturer sources only with Automation and keeps the exact shared check contract', async () => {
    mockConnectedBridge();

    render(<PatchManagementPage />);
    await screen.findByRole('tab', { name: 'Automation' });
    expect(invokeMock.mock.calls.filter((call) => call[1] === 'listVersionSources'))
      .toHaveLength(0);

    await userEvent.click(screen.getByRole('tab', { name: 'Automation' }));
    await waitFor(() => expect(
      invokeMock.mock.calls.filter((call) => call[1] === 'listVersionSources'),
    ).toHaveLength(1));
    await userEvent.click(screen.getByRole('button', { name: 'Run all checks now' }));

    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith(
      'patchmanagement',
      'checkVendorVersions',
      { productIds: null },
    ));
  });

  it('presents a dashboard load failure with opsi guidance, evidence and retry', async () => {
    mockRejectedAction(
      'getDashboard',
      new BridgeInvokeError({
        code: 'AUTHENTICATION_FAILED',
        message: 'JSON-RPC sign-in rejected',
        details: 'HTTP 401 from opsiconfd',
      }),
    );

    render(<PatchManagementPage />);

    expect(await screen.findByText('The patch overview could not be loaded.')).toBeDefined();
    expect(screen.getByText('The supplied credentials were not accepted by the target.')).toBeDefined();
    expect(screen.getByText('opsi rejected the credentials — check user name and password.')).toBeDefined();
    const technicalDetails = screen.getByText('Technical details').closest('details');
    expect(technicalDetails?.hasAttribute('open')).toBe(false);
    expect(screen.getByText(/Code: AUTHENTICATION_FAILED/)).toBeDefined();

    await userEvent.click(screen.getByRole('button', { name: 'Reload overview' }));
    await waitFor(() => expect(
      invokeMock.mock.calls.filter((call) => call[1] === 'getDashboard'),
    ).toHaveLength(2));
  });

  it('presents a connection-status transport failure with opsi guidance and retry', async () => {
    mockRejectedAction(
      'getConnectionStatus',
      new BridgeInvokeError({
        code: 'SERVICE_UNAVAILABLE',
        message: 'opsiconfd did not answer',
        details: 'Connection refused on port 4447',
      }),
    );

    render(<PatchManagementPage />);

    expect(await screen.findByText('The opsi connection status could not be loaded.')).toBeDefined();
    const header = screen.getByRole('heading', { name: 'Patch Management' }).closest('header')!;
    expect(within(header).getByText('Failed')).toBeDefined();
    expect(within(header).getByText('Connection check')).toBeDefined();
    expect(within(header).queryByText('Not connected')).toBeNull();
    expect(screen.getByText('The configured service or local dependency could not be reached or started.')).toBeDefined();
    expect(screen.getByText(/opsiconfd did not answer/, { selector: 'p' })).toBeDefined();
    await userEvent.click(screen.getByRole('button', { name: 'Check connection again' }));
    await waitFor(() => expect(
      invokeMock.mock.calls.filter((call) => call[1] === 'getConnectionStatus'),
    ).toHaveLength(2));
  });

  it('keeps an automatic connection failure out of the primary raw-text surface', async () => {
    mockConnectedBridge();
    const original = invokeMock.getMockImplementation();
    invokeMock.mockImplementation((module: string, action: string, payload?: Record<string, unknown>) =>
      action === 'getConnectionStatus'
        ? Promise.resolve({ ...disconnectedStatus, connectionError: 'TLS certificate chain rejected' })
        : original?.(module, action, payload));

    render(<PatchManagementPage />);

    expect(await screen.findByText('The automatic opsi connection could not be established.')).toBeDefined();
    const header = screen.getByRole('heading', { name: 'Patch Management' }).closest('header')!;
    expect(within(header).getByText('Failed')).toBeDefined();
    expect(within(header).getByText('Connection check')).toBeDefined();
    expect(screen.getByText('The configured opsi connection was not established successfully at startup.')).toBeDefined();
    const technicalDetails = screen.getByText('Technical details').closest('details');
    expect(technicalDetails?.hasAttribute('open')).toBe(false);
    expect(screen.getByText(/Error: TLS certificate chain rejected/)).toBeDefined();
  });

  it('presents an audit-history load failure without claiming that the history is empty', async () => {
    mockRejectedAction('getAuditLog', new Error('Audit database is locked'));

    render(<PatchManagementPage />);
    await userEvent.click(await screen.findByRole('tab', { name: 'History' }));

    expect(await screen.findByText('The patch history could not be loaded.')).toBeDefined();
    expect(screen.queryByText('No Patch Management actions have been recorded yet.')).toBeNull();
    await userEvent.click(screen.getByRole('button', { name: 'Reload history' }));
    await waitFor(() => expect(
      invokeMock.mock.calls.filter((call) => call[1] === 'getAuditLog'),
    ).toHaveLength(2));
  });

  it('presents a mapping-list load failure with a local retry', async () => {
    mockRejectedAction('listMappings', new Error('Mapping database could not be read'));

    render(<PatchManagementPage />);
    await userEvent.click(await screen.findByRole('tab', { name: 'Mappings' }));

    expect(await screen.findByText('The existing software mappings could not be loaded.')).toBeDefined();
    await userEvent.click(screen.getByRole('button', { name: 'Reload mappings' }));
    await waitFor(() => expect(
      invokeMock.mock.calls.filter((call) => call[1] === 'listMappings'),
    ).toHaveLength(2));
  });

  it('presents a manufacturer-source load failure with a local retry', async () => {
    mockRejectedAction('listVersionSources', new Error('Version-source database could not be read'));

    render(<PatchManagementPage />);
    await userEvent.click(await screen.findByRole('tab', { name: 'Automation' }));

    expect(await screen.findByText('The manufacturer sources could not be loaded.')).toBeDefined();
    await userEvent.click(screen.getByRole('button', { name: 'Reload manufacturer sources' }));
    await waitFor(() => expect(
      invokeMock.mock.calls.filter((call) => call[1] === 'listVersionSources'),
    ).toHaveLength(2));
  });

  it('presents a package-workflow load failure instead of an unverified pending state', async () => {
    mockRejectedAction('getPackageWorkflowStatus', new Error('Workflow status unavailable'));

    render(<PatchManagementPage />);
    await userEvent.click(await screen.findByRole('button', { name: /Mozilla Firefox/ }));

    expect(await screen.findByText('The package approval status could not be loaded.')).toBeDefined();
    expect(screen.queryByText('Test pending')).toBeNull();
    await userEvent.click(screen.getByRole('button', { name: 'Reload approval status' }));
    await waitFor(() => expect(
      invokeMock.mock.calls.filter((call) => call[1] === 'getPackageWorkflowStatus'),
    ).toHaveLength(2));
  });

  it('shows approval loading as running instead of claiming that the test is pending', async () => {
    mockConnectedBridge();
    const original = invokeMock.getMockImplementation()!;
    const pendingWorkflow = new Promise(() => {});
    invokeMock.mockImplementation((module: string, action: string, payload?: Record<string, unknown>) =>
      action === 'getPackageWorkflowStatus'
        ? pendingWorkflow
        : original(module, action, payload));

    render(<PatchManagementPage />);
    await userEvent.click(await screen.findByRole('button', { name: /Mozilla Firefox/ }));

    expect(await screen.findByText('Running')).toBeDefined();
    expect(screen.getByText('Approval status')).toBeDefined();
    expect(screen.queryByText('Test pending')).toBeNull();
  });

  it('discards a late approval response after switching to another package', async () => {
    const secondProduct = {
      ...dashboard.products[0],
      productId: 'sevenzip',
      name: '7-Zip',
    };
    const twoProductDashboard = {
      ...dashboard,
      summary: { ...dashboard.summary, productCount: 2 },
      products: [...dashboard.products, secondProduct],
    };
    mockConnectedBridge(twoProductDashboard);
    const original = invokeMock.getMockImplementation()!;
    let resolveFirefox!: (value: unknown) => void;
    const firefoxWorkflow = new Promise((resolve) => { resolveFirefox = resolve; });
    invokeMock.mockImplementation((module: string, action: string, payload?: Record<string, unknown>) => {
      if (action !== 'getPackageWorkflowStatus') return original(module, action, payload);
      if (payload?.productId === 'firefox') return firefoxWorkflow;
      return Promise.resolve({
        productId: 'sevenzip',
        testDepotId: null,
        testedVersion: null,
        testUpdateSucceededAtUtc: null,
        pilotApprovedAtUtc: null,
        pilotApproved: false,
        lastSynchronizationResult: null,
        lastSynchronizationAtUtc: null,
        lastError: null,
      });
    });

    render(<PatchManagementPage />);
    await userEvent.click(await screen.findByRole('button', { name: /Mozilla Firefox/ }));
    expect(await screen.findByText('Running')).toBeDefined();
    await userEvent.click(screen.getByRole('button', { name: /7-Zip/ }));
    await waitFor(() => expect(
      screen.getAllByText('Pending').some((element) => element.tagName === 'SPAN'),
    ).toBe(true));
    expect(screen.getByText('Test update')).toBeDefined();

    resolveFirefox({
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

    await waitFor(() => expect(screen.queryByText('Pilot approved')).toBeNull());
    expect(screen.getAllByText('Pending').some((element) => element.tagName === 'SPAN')).toBe(true);
  });

  it('presents a fleet-client page failure with a local retry', async () => {
    mockRejectedAction('listClientStates', new Error('Page query failed'));

    render(<PatchManagementPage />);
    await userEvent.click(await screen.findByRole('tab', { name: 'Clients (2)' }));

    expect(await screen.findByText('The patch client list could not be loaded.')).toBeDefined();
    await userEvent.click(screen.getByRole('button', { name: 'Reload client list' }));
    await waitFor(() => expect(
      invokeMock.mock.calls.filter((call) => call[1] === 'listClientStates'),
    ).toHaveLength(2));
  });

  it('presents a product-client page failure with a local retry', async () => {
    mockRejectedAction('listClientStates', new Error('Product page query failed'));

    render(<PatchManagementPage />);
    await userEvent.click(await screen.findByRole('button', { name: /Mozilla Firefox/ }));

    expect(await screen.findByText('The package client details could not be loaded.')).toBeDefined();
    await userEvent.click(screen.getByRole('button', { name: 'Reload package clients' }));
    await waitFor(() => expect(
      invokeMock.mock.calls.filter((call) => call[1] === 'listClientStates'),
    ).toHaveLength(2));
  });

  it('shows the unique client count and short client names', async () => {
    mockConnectedBridge();

    render(<PatchManagementPage />);

    const clientsTab = await screen.findByRole('tab', { name: 'Clients (2)' });
    await userEvent.click(clientsTab);

    expect(screen.getByText('2 unique clients · 1 of 1 package state')).toBeDefined();
    const clientName = screen.getByText('pc1');
    expect(clientName.getAttribute('title')).toBe('pc1.kauth.local');
    expect(screen.queryByText('pc1.kauth.local')).toBeNull();
  });

  it('paginates, sorts and filters large client package-state lists', async () => {
    const largeDashboard: PatchDashboardOverview = {
      ...dashboard,
      summary: { ...dashboard.summary, clientCount: 101 },
    };
    const clientStates = Array.from({ length: 101 }, (_, index): PatchClientState => ({
      ...patchClientState,
      clientId: `pc${String(index + 1).padStart(3, '0')}.kauth.local`,
      state: index % 2 === 0 ? 'FAILED' : 'COMPLETED',
    }));
    mockConnectedBridge(largeDashboard, clientStates);

    render(<PatchManagementPage />);
    await userEvent.click(await screen.findByRole('tab', { name: 'Clients (101)' }));

    expect(await screen.findByText('1–50 of 101')).toBeDefined();
    expect(screen.getByText('pc001')).toBeDefined();
    expect(screen.queryByText('pc051')).toBeNull();

    await userEvent.click(screen.getByRole('button', { name: 'Next' }));
    expect(await screen.findByText('51–100 of 101')).toBeDefined();
    expect(screen.getByText('pc051')).toBeDefined();
    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith(
      'patchmanagement',
      'listClientStates',
      expect.objectContaining({ page: 2, pageSize: 50, sortColumn: 'client', sortDirection: 'asc' }),
    ));

    await userEvent.click(screen.getByRole('button', { name: /Client/ }));
    expect(await screen.findByText('1–50 of 101')).toBeDefined();
    expect(screen.getByText('pc101')).toBeDefined();

    await userEvent.selectOptions(
      screen.getByRole('combobox', { name: 'Filter patch clients by status' }),
      'FAILED',
    );
    expect(await screen.findByText('1–50 of 51')).toBeDefined();
    expect(screen.getByText(/51 of 101 package states/)).toBeDefined();

    await userEvent.clear(screen.getByRole('searchbox', { name: 'Filter patch clients' }));
    await userEvent.type(screen.getByRole('searchbox', { name: 'Filter patch clients' }), 'pc001');
    expect(await screen.findByText('1–1 of 1')).toBeDefined();
    expect(screen.getByText('pc001')).toBeDefined();
  });

  it('loads product client pages and keeps selections across pages', async () => {
    const clientStates = Array.from({ length: 101 }, (_, index): PatchClientState => ({
      ...patchClientState,
      clientId: `pc${String(index + 1).padStart(3, '0')}.kauth.local`,
    }));
    mockConnectedBridge(dashboard, clientStates);

    render(<PatchManagementPage />);
    await userEvent.click(await screen.findByRole('button', { name: /Mozilla Firefox/ }));

    expect(await screen.findByText('1–50 of 101')).toBeDefined();
    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith(
      'patchmanagement',
      'listClientStates',
      expect.objectContaining({ productId: 'firefox', page: 1, pageSize: 50 }),
    ));
    await userEvent.click(screen.getByRole('checkbox', { name: 'Select client pc001' }));
    await userEvent.click(screen.getByRole('button', { name: 'Next' }));
    expect(await screen.findByText('51–100 of 101')).toBeDefined();
    await userEvent.click(screen.getByRole('checkbox', { name: 'Select client pc051' }));

    await userEvent.click(screen.getByRole('button', { name: /Prepare deployment/ }));
    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith(
      'patchmanagement',
      'getRolloutPreview',
      expect.objectContaining({
        productId: 'firefox',
        clientIds: ['pc001.kauth.local', 'pc051.kauth.local'],
      }),
    ));
  });

  it('translates product count drill-downs into server filters', async () => {
    mockConnectedBridge();

    render(<PatchManagementPage />);
    await userEvent.click(await screen.findByTitle('Show the outdated clients'));

    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith(
      'patchmanagement',
      'listClientStates',
      expect.objectContaining({
        productId: 'firefox',
        state: 'UPDATE_AVAILABLE',
        installationStatus: null,
        page: 1,
      }),
    ));
    expect((await screen.findAllByText('Outdated')).length).toBeGreaterThan(1);
  });

  it('presents a failed deployment preview with opsi guidance and technical evidence', async () => {
    mockRejectedAction(
      'getRolloutPreview',
      new BridgeInvokeError({
        code: 'SERVICE_UNAVAILABLE',
        message: 'opsiconfd did not answer',
        details: 'Connection refused on port 4447',
      }),
    );

    render(<PatchManagementPage />);
    await userEvent.click(await screen.findByRole('button', { name: /Mozilla Firefox/ }));
    await userEvent.click(screen.getByRole('button', { name: /Prepare deployment/ }));

    expect(await screen.findByText('The deployment preview could not be created.')).toBeDefined();
    expect(screen.getByText('The configured service or local dependency could not be reached or started.')).toBeDefined();
    expect(screen.getByText(/opsiconfd did not answer/, { selector: 'p' })).toBeDefined();
    const technicalDetails = screen.getByText('Technical details').closest('details');
    expect(technicalDetails?.hasAttribute('open')).toBe(false);
    expect(screen.getByText(/Code: SERVICE_UNAVAILABLE/)).toBeDefined();
  });

  it('treats an uncertain rollout result as a verification task instead of an automatic retry', async () => {
    mockRejectedAction('requestRollout', new Error('Connection closed after request submission'));

    render(<PatchManagementPage />);
    await userEvent.click(await screen.findByRole('button', { name: /Mozilla Firefox/ }));
    await userEvent.click(screen.getByRole('button', { name: /Prepare deployment/ }));
    await userEvent.click(await screen.findByRole('checkbox', { name: /I have reviewed the affected clients/ }));
    await userEvent.click(screen.getByRole('button', { name: /Request deployment for 1 client/ }));

    expect(await screen.findByText('The deployment request could not be confirmed as completed.')).toBeDefined();
    expect(screen.getByText(/First check the history and current opsi state/)).toBeDefined();
    expect(invokeMock.mock.calls.filter((call) => call[1] === 'requestRollout')).toHaveLength(1);
  });

  it('keeps a failed confirmed package action reviewable and does not retry it automatically', async () => {
    mockRejectedAction('executePackageUpdate', new Error('SSH result was lost'));

    render(<PatchManagementPage />);
    await userEvent.click(await screen.findByRole('button', { name: /Mozilla Firefox/ }));
    await userEvent.click(screen.getByRole('button', { name: 'Prepare test update' }));
    await userEvent.click(await screen.findByRole('checkbox', { name: /I want to update 'firefox'/ }));
    await userEvent.click(screen.getByRole('button', { name: 'Confirm and run over SSH' }));

    expect(await screen.findByText('The package action could not be confirmed as completed.')).toBeDefined();
    expect(screen.getByText(/First check the history and package status on the target depots/)).toBeDefined();
    expect(screen.getByText('opsi-package-updater -v update firefox')).toBeDefined();
    expect(invokeMock.mock.calls.filter((call) => call[1] === 'executePackageUpdate')).toHaveLength(1);
  });

  it('requires preview and explicit confirmation before requesting a rollout', async () => {
    mockConnectedBridge();

    render(<PatchManagementPage />);
    await userEvent.click(await screen.findByRole('button', { name: /Mozilla Firefox/ }));
    await userEvent.click(screen.getByRole('button', { name: /Prepare deployment/ }));

    // Preview shown, nothing sent yet
    expect(await screen.findByText(/Nothing has been sent to opsi yet/)).toBeDefined();
    const rolloutButton = screen.getByRole('button', { name: /Request deployment for 1 client/ });
    expect(rolloutButton.hasAttribute('disabled')).toBe(true);
    expect(invokeMock.mock.calls.some((call) => call[1] === 'requestRollout')).toBe(false);

    await userEvent.click(screen.getByRole('checkbox', { name: /I have reviewed the affected clients/ }));
    await userEvent.click(rolloutButton);

    await waitFor(() =>
      expect(
        invokeMock.mock.calls.find((call) => call[1] === 'requestRollout')?.[2],
      ).toMatchObject({ productId: 'firefox', confirmed: true, clientIds: ['pc1.kauth.local'] }),
    );
    expect(await screen.findByText(/Deployment requested for 1 client/)).toBeDefined();
  });

  it('requires an SSH preview and explicit confirmation for a test-depot update', async () => {
    mockConnectedBridge();

    render(<PatchManagementPage />);
    await userEvent.click(await screen.findByRole('button', { name: /Mozilla Firefox/ }));
    await userEvent.click(screen.getByRole('button', { name: 'Prepare test update' }));

    expect(await screen.findByText('opsi-package-updater -v update firefox')).toBeDefined();
    const executeButton = screen.getByRole('button', { name: 'Confirm and run over SSH' });
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
    expect(await screen.findByText(/1 succeeded/)).toBeDefined();
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
    const syncButton = await screen.findByRole('button', { name: 'Distribute to additional depots' });
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

  it('renders audit results semantically without exposing technical result values', async () => {
    mockConnectedBridge();
    const original = invokeMock.getMockImplementation()!;
    invokeMock.mockImplementation((module: string, action: string, payload?: Record<string, unknown>) => {
      if (action === 'getAuditLog') {
        const entry = {
          timestampUtc: '2026-07-03T11:00:00Z',
          userName: 'vinz',
          productId: 'firefox',
          depotId: 'depot-denkingen.kauth.local',
          targetClients: [],
          previewJson: null,
          errorMessage: null,
        };
        return Promise.resolve({
          entries: [
            { ...entry, id: 1, action: 'PREPARE_PACKAGES', result: 'PLANNED' },
            { ...entry, id: 2, action: 'ROLLOUT_REQUESTED', result: 'SUCCESS' },
            { ...entry, id: 3, action: 'PACKAGE_UPDATE', result: 'FAILED', errorMessage: 'opsi failed' },
          ],
        });
      }
      return original(module, action, payload);
    });

    render(<PatchManagementPage />);

    await userEvent.click(await screen.findByRole('tab', { name: 'History' }));
    const previewRow = (await screen.findByText('PREPARE_PACKAGES')).closest('tr')!;
    expect(within(previewRow).getByText('Succeeded')).toBeDefined();
    expect(within(previewRow).getByText('Preview created')).toBeDefined();

    const successRow = screen.getByText('ROLLOUT_REQUESTED').closest('tr')!;
    expect(within(successRow).getByText('Succeeded')).toBeDefined();

    const failureRow = screen.getByText('PACKAGE_UPDATE').closest('tr')!;
    expect(within(failureRow).getByText('Failed')).toBeDefined();
    expect(screen.queryByText(/^(SUCCESS|FAILED|PLANNED)$/)).toBeNull();
  });

  it('offers mapping inputs for unmapped inventory software', async () => {
    mockConnectedBridge();

    render(<PatchManagementPage />);

    await userEvent.click(await screen.findByRole('tab', { name: 'Mappings' }));
    expect(await screen.findByText('Notepad++')).toBeDefined();
    const input = screen.getByLabelText('opsi product id for Notepad++');
    expect((input as HTMLInputElement).value).toBe('');
    // Without a product id the map action stays disabled
    expect(screen.getByRole('button', { name: 'Map' }).hasAttribute('disabled')).toBe(true);
  });

  it('keeps a failed mapping save reviewable and does not retry it automatically', async () => {
    mockRejectedAction(
      'saveMapping',
      new BridgeInvokeError({
        code: 'INTERNAL_ERROR',
        message: 'Mapping result was lost',
        details: 'Database commit outcome is unknown',
      }),
    );

    render(<PatchManagementPage />);
    await userEvent.click(await screen.findByRole('tab', { name: 'Mappings' }));
    await userEvent.type(screen.getByLabelText('opsi product id for Notepad++'), 'npp');
    await userEvent.click(screen.getByRole('button', { name: 'Map' }));

    expect(await screen.findByText('The software mapping could not be confirmed as saved.')).toBeDefined();
    expect(screen.getByText(/First check Existing mappings and the history/)).toBeDefined();
    expect((screen.getByLabelText('opsi product id for Notepad++') as HTMLInputElement).value).toBe('npp');
    expect(screen.getByText('Technical details').closest('details')?.hasAttribute('open')).toBe(false);
    expect(invokeMock.mock.calls.filter((call) => call[1] === 'saveMapping')).toHaveLength(1);
  });

  it('treats a failed mapping deletion as a state-verification task', async () => {
    mockConnectedBridge();
    const original = invokeMock.getMockImplementation();
    invokeMock.mockImplementation((module: string, action: string, payload?: Record<string, unknown>) => {
      if (action === 'listMappings') {
        return Promise.resolve({ mappings: [{ softwareName: 'Notepad++', opsiProductId: 'npp' }] });
      }
      if (action === 'deleteMapping') return Promise.reject(new Error('Delete result was lost'));
      return original?.(module, action, payload);
    });

    render(<PatchManagementPage />);
    await userEvent.click(await screen.findByRole('tab', { name: 'Mappings' }));
    await userEvent.click(await screen.findByText('Existing mappings (1)'));
    await userEvent.click(screen.getByRole('button', { name: 'Remove' }));

    expect(await screen.findByText('The software mapping could not be confirmed as removed.')).toBeDefined();
    expect(screen.getByText(/First check the existing mappings and history/)).toBeDefined();
    expect(invokeMock.mock.calls.filter((call) => call[1] === 'deleteMapping')).toHaveLength(1);
  });

  it('treats a failed manufacturer check as a last-check verification task', async () => {
    mockRejectedAction('checkVendorVersions', new Error('Check completion is unknown'));

    render(<PatchManagementPage />);
    await userEvent.click(await screen.findByRole('tab', { name: 'Automation' }));
    await userEvent.click(screen.getByRole('button', { name: 'Run all checks now' }));

    expect(await screen.findByText('The manufacturer checks could not be confirmed as completed.')).toBeDefined();
    expect(screen.getByText(/First check the latest check times and history/)).toBeDefined();
    expect(invokeMock.mock.calls.filter((call) => call[1] === 'checkVendorVersions')).toHaveLength(1);
  });

  it('keeps a failed manufacturer-source save form reviewable', async () => {
    mockRejectedAction('saveVersionSource', new Error('Save result was lost'));

    render(<PatchManagementPage />);
    await userEvent.click(await screen.findByRole('tab', { name: 'Automation' }));
    await userEvent.selectOptions(
      screen.getByRole('combobox', { name: 'opsi product for manufacturer source' }),
      'firefox',
    );
    await userEvent.type(screen.getByLabelText('Manufacturer source HTTPS URL'), 'https://vendor.example/releases');
    await userEvent.type(screen.getByLabelText('Version pattern'), 'Version (1.2.3)');
    await userEvent.click(screen.getByRole('button', { name: 'Save source' }));

    expect(await screen.findByText('The manufacturer source could not be confirmed as saved.')).toBeDefined();
    expect(screen.getByText(/First check the manufacturer sources/)).toBeDefined();
    expect((screen.getByLabelText('Manufacturer source HTTPS URL') as HTMLInputElement).value)
      .toBe('https://vendor.example/releases');
    expect(invokeMock.mock.calls.filter((call) => call[1] === 'saveVersionSource')).toHaveLength(1);
  });

  it('treats a failed manufacturer-source deletion as a state-verification task', async () => {
    mockConnectedBridge();
    const original = invokeMock.getMockImplementation();
    invokeMock.mockImplementation((module: string, action: string, payload?: Record<string, unknown>) => {
      if (action === 'listVersionSources') {
        return Promise.resolve({ sources: [{
          productId: 'firefox',
          sourceUrl: 'https://vendor.example/releases',
          versionPattern: 'Version ([0-9.]+)',
          enabled: true,
          latestVersion: '129.0',
          lastCheckedUtc: '2026-07-03T11:55:00Z',
          checkStatus: 'SUCCESS',
          lastError: null,
        }] });
      }
      if (action === 'deleteVersionSource') return Promise.reject(new Error('Delete result was lost'));
      return original?.(module, action, payload);
    });

    render(<PatchManagementPage />);
    await userEvent.click(await screen.findByRole('tab', { name: 'Automation' }));
    await userEvent.click(await screen.findByRole('button', { name: 'Remove' }));

    expect(await screen.findByText('The manufacturer source could not be confirmed as removed.')).toBeDefined();
    expect(screen.getByText(/First check the manufacturer sources and history/)).toBeDefined();
    expect(invokeMock.mock.calls.filter((call) => call[1] === 'deleteVersionSource')).toHaveLength(1);
  });

  it('configures an auditable manufacturer version source', async () => {
    mockConnectedBridge();
    render(<PatchManagementPage />);

    await userEvent.click(await screen.findByRole('tab', { name: 'Automation' }));
    await userEvent.selectOptions(
      screen.getByRole('combobox', { name: 'opsi product for manufacturer source' }),
      'firefox',
    );
    await userEvent.type(screen.getByLabelText('Manufacturer source HTTPS URL'), 'https://vendor.example/releases');
    await userEvent.type(screen.getByLabelText('Version pattern'), 'Version (1.2.3)');
    await userEvent.click(screen.getByRole('button', { name: 'Save source' }));

    await waitFor(() => expect(
      invokeMock.mock.calls.find((call) => call[1] === 'saveVersionSource')?.[2],
    ).toMatchObject({ productId: 'firefox', sourceUrl: 'https://vendor.example/releases' }));
    expect(await screen.findByText('Unknown')).toBeDefined();
    expect(screen.getByText('Not checked')).toBeDefined();
    expect(screen.queryByText('NOT_CHECKED')).toBeNull();
  });

  it('distinguishes a configured but unchecked manufacturer source in package details', async () => {
    const notCheckedDashboard = structuredClone(dashboard);
    notCheckedDashboard.products[0].manufacturerVersion = null;
    notCheckedDashboard.products[0].manufacturerCheckStatus = 'NOT_CHECKED';
    notCheckedDashboard.products[0].manufacturerCheckedAtUtc = null;
    mockConnectedBridge(notCheckedDashboard);

    render(<PatchManagementPage />);
    await userEvent.click(await screen.findByRole('button', { name: /Mozilla Firefox/ }));

    const checkButton = screen.getByRole('button', { name: 'Check version' });
    const manufacturerPanel = checkButton.closest('div.rounded') as HTMLDivElement;
    expect(within(manufacturerPanel).getByText('Unknown')).toBeDefined();
    expect(within(manufacturerPanel).getByText('Not checked')).toBeDefined();
    expect(within(manufacturerPanel).getByText('The configured manufacturer source has not been checked yet.')).toBeDefined();
    expect(within(manufacturerPanel).queryByText('No manufacturer source is configured for this package. No version is estimated.')).toBeNull();
  });

  it('counts only enabled manufacturer sources as active and marks disabled sources explicitly', async () => {
    mockConnectedBridge();
    const original = invokeMock.getMockImplementation();
    invokeMock.mockImplementation((module: string, action: string, payload?: Record<string, unknown>) => {
      if (action === 'listVersionSources') {
        return Promise.resolve({ sources: [
          {
            productId: 'firefox', sourceUrl: 'https://vendor.example/firefox', versionPattern: '([0-9.]+)',
            enabled: true, latestVersion: '129.0', lastCheckedUtc: '2026-07-03T11:55:00Z',
            checkStatus: 'SUCCESS', lastError: null,
          },
          {
            productId: 'sevenzip', sourceUrl: 'https://vendor.example/sevenzip', versionPattern: '([0-9.]+)',
            enabled: false, latestVersion: '24.0', lastCheckedUtc: '2026-07-02T11:55:00Z',
            checkStatus: 'SUCCESS', lastError: null,
          },
        ] });
      }
      return original?.(module, action, payload);
    });

    render(<PatchManagementPage />);
    await userEvent.click(await screen.findByRole('tab', { name: 'Automation' }));

    expect(await screen.findByText('1 active')).toBeDefined();
    expect(screen.queryByText('2 active')).toBeNull();
    const disabledRow = screen.getByText('sevenzip').closest('tr') as HTMLTableRowElement;
    expect(within(disabledRow).getByText('Disabled')).toBeDefined();
  });
});
