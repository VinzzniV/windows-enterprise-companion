import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { PatchDashboardOverview } from '../../shared/api-types';
import { PatchProductOverviewWorkspace } from './PatchProductOverviewWorkspace';

vi.mock('./PatchProductDetailsPanel', () => ({
  PatchProductDetailsPanel: (props: {
    product: { productId: string };
    clientFilter: string | null;
    selectedClients: ReadonlySet<string>;
    versionCheckBusy: boolean;
    onClose: () => void;
    onClearClientFilter: () => void;
    onToggleClient: (clientId: string) => void;
    onCheckVersion: (productId: string) => void;
    onDashboardRefresh: () => void;
  }) => (
    <section aria-label="Mocked product details">
      Details {props.product.productId} · {props.clientFilter ?? 'all'} · {props.selectedClients.size} selected · {props.versionCheckBusy ? 'busy' : 'idle'}
      <button type="button" onClick={props.onClose}>Close details</button>
      <button type="button" onClick={props.onClearClientFilter}>Clear detail filter</button>
      <button type="button" onClick={() => props.onToggleClient('client-2')}>Toggle detail client</button>
      <button type="button" onClick={() => props.onCheckVersion(props.product.productId)}>Check detail version</button>
      <button type="button" onClick={props.onDashboardRefresh}>Refresh detail dashboard</button>
    </section>
  ),
}));

const dashboard: PatchDashboardOverview = {
  serverUrl: 'https://opsi.example.test:4447/',
  depotFilter: null,
  generatedAtUtc: '2026-08-20T08:00:00Z',
  summary: {
    productCount: 2,
    productsWithUpdates: 1,
    productsWithDepotDeviation: 1,
    productsMissingOnDepots: 1,
    productsWithFailures: 1,
    pendingRolloutCount: 1,
    outdatedClientCount: 4,
    clientCount: 5,
    depotCount: 2,
    unmappedSoftwareCount: 0,
  },
  depots: [
    { id: 'depot-main', description: 'Main server', isConfigServer: true, clientCount: 3 },
    { id: 'depot-test', description: 'Test depot', isConfigServer: false, clientCount: 2 },
  ],
  products: [
    {
      productId: 'firefox',
      name: 'Mozilla Firefox',
      availableVersion: '128.0-2',
      referenceVersion: '128.0-2',
      manufacturerVersion: '129.0',
      manufacturerCheckStatus: 'SUCCESS',
      manufacturerCheckedAtUtc: '2026-08-20T07:55:00Z',
      manufacturerCheckError: null,
      manufacturerUpdateAvailable: true,
      depotVersions: [{ depotId: 'depot-main', version: '128.0-2' }],
      missingDepotIds: ['depot-test'],
      packageStatus: 'UPDATE_AVAILABLE',
      state: 'UPDATE_AVAILABLE',
      installedClientCount: 3,
      outdatedClientCount: 2,
      failedClientCount: 0,
      pendingActionCount: 1,
      lastError: null,
      mappedSoftwareNames: ['Mozilla Firefox'],
      inventoryDetections: [],
    },
    {
      productId: '7zip',
      name: '7-Zip',
      availableVersion: '25.01-1',
      referenceVersion: '25.01-1',
      manufacturerVersion: null,
      manufacturerCheckStatus: 'FAILED',
      manufacturerCheckedAtUtc: '2026-08-20T07:50:00Z',
      manufacturerCheckError: 'Vendor endpoint unavailable.',
      manufacturerUpdateAvailable: false,
      depotVersions: [
        { depotId: 'depot-main', version: '25.01-1' },
        { depotId: 'depot-test', version: '24.09-1' },
      ],
      missingDepotIds: [],
      packageStatus: 'CHECK_FAILED',
      state: 'FAILED',
      installedClientCount: 2,
      outdatedClientCount: 2,
      failedClientCount: 1,
      pendingActionCount: 0,
      lastError: 'Vendor endpoint unavailable.',
      mappedSoftwareNames: ['7-Zip'],
      inventoryDetections: [],
    },
  ],
  unmappedSoftware: [],
};

function renderWorkspace(overrides: Partial<React.ComponentProps<typeof PatchProductOverviewWorkspace>> = {}) {
  const props: React.ComponentProps<typeof PatchProductOverviewWorkspace> = {
    connected: true,
    dashboard,
    depotFilter: '',
    selectedProductId: null,
    clientFilter: null,
    selectedClients: new Set(),
    versionCheckBusy: false,
    onSelectProduct: vi.fn(),
    onDrillIntoClients: vi.fn(),
    onClearClientFilter: vi.fn(),
    onToggleClient: vi.fn(),
    onCheckVersion: vi.fn(),
    onDashboardRefresh: vi.fn(),
    ...overrides,
  };
  return { ...render(<PatchProductOverviewWorkspace {...props} />), props };
}

describe('PatchProductOverviewWorkspace', () => {
  it('presents package KPIs, action evidence and the complete product table', () => {
    renderWorkspace();

    expect(screen.getByText('Packages')).toBeDefined();
    expect(screen.getByText('Pending deployments')).toBeDefined();
    expect(screen.getByText('Action required')).toBeDefined();
    expect(screen.getByText('1 failed packages')).toBeDefined();
    expect(screen.getByText('1 packages missing from depots')).toBeDefined();
    expect(screen.getByText('1 version deviations')).toBeDefined();
    expect(screen.getByText('Mozilla Firefox')).toBeDefined();
    expect(screen.getByText('7-Zip')).toBeDefined();
    expect(screen.getByText('2 of 2 packages')).toBeDefined();
  });

  it('owns package search and status filtering without changing the dashboard input', async () => {
    renderWorkspace();

    const search = screen.getByLabelText('Search packages');
    await userEvent.type(search, 'mozilla');
    expect(screen.getByText('Mozilla Firefox')).toBeDefined();
    expect(screen.queryByText('7-Zip')).toBeNull();
    expect(screen.getByText('1 of 2 packages')).toBeDefined();

    await userEvent.clear(search);
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Status filter' }), 'CHECK_FAILED');
    expect(screen.queryByText('Mozilla Firefox')).toBeNull();
    expect(screen.getByText('7-Zip')).toBeDefined();
    expect(dashboard.products).toHaveLength(2);
  });

  it('forwards controlled selection, drill and detail callbacks exactly', async () => {
    const view = renderWorkspace({
      selectedProductId: 'firefox',
      clientFilter: 'UPDATE_AVAILABLE',
      selectedClients: new Set(['client-1']),
      versionCheckBusy: true,
    });

    await userEvent.click(screen.getByRole('button', { name: /7-Zip/ }));
    await userEvent.click(screen.getAllByTitle('Show the installed clients')[0]);
    await userEvent.click(screen.getAllByTitle('Show the outdated clients')[0]);
    const details = screen.getByRole('region', { name: 'Mocked product details' });
    expect(within(details).getByText(/Details firefox · UPDATE_AVAILABLE · 1 selected · busy/)).toBeDefined();
    await userEvent.click(within(details).getByRole('button', { name: 'Close details' }));
    await userEvent.click(within(details).getByRole('button', { name: 'Clear detail filter' }));
    await userEvent.click(within(details).getByRole('button', { name: 'Toggle detail client' }));
    await userEvent.click(within(details).getByRole('button', { name: 'Check detail version' }));
    await userEvent.click(within(details).getByRole('button', { name: 'Refresh detail dashboard' }));

    expect(view.props.onSelectProduct).toHaveBeenNthCalledWith(1, '7zip');
    expect(view.props.onSelectProduct).toHaveBeenNthCalledWith(2, 'firefox');
    expect(view.props.onDrillIntoClients).toHaveBeenNthCalledWith(1, 'firefox', 'installed');
    expect(view.props.onDrillIntoClients).toHaveBeenNthCalledWith(2, 'firefox', 'UPDATE_AVAILABLE');
    expect(view.props.onClearClientFilter).toHaveBeenCalledTimes(1);
    expect(view.props.onToggleClient).toHaveBeenCalledWith('client-2');
    expect(view.props.onCheckVersion).toHaveBeenCalledWith('firefox');
    expect(view.props.onDashboardRefresh).toHaveBeenCalledTimes(1);
  });
});
