import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { PatchDashboardOverview } from '../../shared/api-types';
import { PatchProductDetailsPanel } from './PatchProductDetailsPanel';

vi.mock('./PatchProductClientsTable', () => ({
  PatchProductClientsTable: (props: {
    productId: string;
    filter: string | null;
    selectedClients: ReadonlySet<string>;
    onClearFilter: () => void;
    onToggleClient: (clientId: string) => void;
  }) => (
    <section aria-label="Mocked product clients">
      Clients {props.productId} · {props.filter ?? 'all'} · {props.selectedClients.size} selected
      <button type="button" onClick={props.onClearFilter}>Clear client filter</button>
      <button type="button" onClick={() => props.onToggleClient('client-2')}>Toggle client-2</button>
    </section>
  ),
}));

vi.mock('./PatchDeploymentWorkflow', () => ({
  PatchDeploymentWorkflow: (props: {
    productId: string;
    depotFilter: string;
    selectedClients: ReadonlySet<string>;
  }) => (
    <div>
      Deployment {props.productId} · {props.depotFilter || 'all depots'} · {props.selectedClients.size} selected
    </div>
  ),
}));

vi.mock('./PatchPackageApprovalWorkflow', () => ({
  PatchPackageApprovalWorkflow: (props: {
    productId: string;
    depots: readonly { id: string }[];
    children?: React.ReactNode;
  }) => (
    <section aria-label="Mocked package approval">
      Approval {props.productId} · {props.depots.length} depots
      {props.children}
    </section>
  ),
}));

const dashboard: PatchDashboardOverview = {
  serverUrl: 'https://opsi.example.test:4447/',
  depotFilter: null,
  generatedAtUtc: '2026-08-20T08:00:00Z',
  summary: {
    productCount: 1,
    productsWithUpdates: 1,
    productsWithDepotDeviation: 0,
    productsMissingOnDepots: 1,
    productsWithFailures: 0,
    pendingRolloutCount: 0,
    outdatedClientCount: 1,
    clientCount: 2,
    depotCount: 2,
    unmappedSoftwareCount: 0,
  },
  depots: [
    { id: 'depot-main', description: 'Main server', isConfigServer: true, clientCount: 0 },
    { id: 'depot-test', description: 'Test depot', isConfigServer: false, clientCount: 2 },
  ],
  products: [{
    productId: 'firefox',
    name: 'Mozilla Firefox',
    availableVersion: '128.0-2',
    referenceVersion: '128.0-2',
    manufacturerVersion: '129.0',
    manufacturerCheckStatus: 'SUCCESS',
    manufacturerCheckedAtUtc: '2026-08-20T07:55:00Z',
    manufacturerCheckError: null,
    manufacturerUpdateAvailable: true,
    depotVersions: [{ depotId: 'depot-test', version: '128.0-2' }],
    missingDepotIds: ['depot-main'],
    packageStatus: 'UPDATE_AVAILABLE',
    state: 'UPDATE_AVAILABLE',
    installedClientCount: 2,
    outdatedClientCount: 1,
    failedClientCount: 0,
    pendingActionCount: 0,
    lastError: 'Repository evidence is incomplete.',
    mappedSoftwareNames: ['Mozilla Firefox'],
    inventoryDetections: [{ host: 'HOST-A', version: '127.0' }],
  }],
  unmappedSoftware: [],
};

const product = dashboard.products[0];

function renderPanel(overrides: Partial<React.ComponentProps<typeof PatchProductDetailsPanel>> = {}) {
  const props: React.ComponentProps<typeof PatchProductDetailsPanel> = {
    connected: true,
    dashboard,
    product,
    depotFilter: '',
    clientFilter: 'UPDATE_AVAILABLE',
    selectedClients: new Set(['client-1']),
    versionCheckBusy: false,
    onClose: vi.fn(),
    onClearClientFilter: vi.fn(),
    onToggleClient: vi.fn(),
    onCheckVersion: vi.fn(),
    onDashboardRefresh: vi.fn(),
    ...overrides,
  };
  return { ...render(<PatchProductDetailsPanel {...props} />), props };
}

describe('PatchProductDetailsPanel', () => {
  it('presents package, depot, manufacturer and inventory evidence in one detail boundary', async () => {
    renderPanel();

    expect(screen.getByText('Package details — Mozilla Firefox')).toBeDefined();
    expect(screen.getByText('Repository evidence is incomplete.')).toBeDefined();
    expect(screen.getByText('Update available')).toBeDefined();
    expect(screen.getByText('firefox')).toBeDefined();
    expect(screen.getByText('Main server')).toBeDefined();
    expect(screen.getByText('Package missing')).toBeDefined();
    expect(screen.getByText('Test depot')).toBeDefined();
    expect(screen.getByText('128.0-2')).toBeDefined();
    expect(screen.getByText(/Latest version: 129\.0/)).toBeDefined();

    await userEvent.click(screen.getByText('WEC inventory detections (1)'));
    expect(screen.getByText('HOST-A: 127.0')).toBeDefined();
  });

  it('honors the depot scope and exposes exact close and manufacturer-check controls', async () => {
    const onClose = vi.fn();
    const onCheckVersion = vi.fn();
    const view = renderPanel({ depotFilter: 'depot-main', onClose, onCheckVersion });

    expect(screen.getByText('Main server')).toBeDefined();
    expect(screen.queryByText('Test depot')).toBeNull();
    await userEvent.click(screen.getByRole('button', { name: 'Close' }));
    await userEvent.click(screen.getByRole('button', { name: 'Check version' }));
    expect(onClose).toHaveBeenCalledTimes(1);
    expect(onCheckVersion).toHaveBeenCalledWith('firefox');

    view.rerender(
      <PatchProductDetailsPanel
        {...view.props}
        connected={false}
        versionCheckBusy
      />,
    );
    const busyCheck = screen.getByRole('button', { name: 'Checking…' });
    expect(busyCheck.hasAttribute('disabled')).toBe(true);
  });

  it('composes the controlled client, approval and deployment scopes without taking ownership', async () => {
    const onClearClientFilter = vi.fn();
    const onToggleClient = vi.fn();
    renderPanel({ onClearClientFilter, onToggleClient });

    const clients = screen.getByRole('region', { name: 'Mocked product clients' });
    expect(within(clients).getByText(/Clients firefox · UPDATE_AVAILABLE · 1 selected/)).toBeDefined();
    expect(screen.getByText('Approval firefox · 2 depots')).toBeDefined();
    expect(screen.getByText('Deployment firefox · all depots · 1 selected')).toBeDefined();
    await userEvent.click(screen.getByRole('button', { name: 'Clear client filter' }));
    await userEvent.click(screen.getByRole('button', { name: 'Toggle client-2' }));
    expect(onClearClientFilter).toHaveBeenCalledTimes(1);
    expect(onToggleClient).toHaveBeenCalledWith('client-2');
  });
});
