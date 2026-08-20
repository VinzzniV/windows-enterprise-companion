import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type {
  PatchClientStatePage,
  PatchDashboardOverview,
} from '../../shared/api-types';
import { PatchProductClientsTable } from './PatchProductClientsTable';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../shared/bridge/bridgeClient', async (importOriginal) => {
  const original = await importOriginal<typeof import('../../shared/bridge/bridgeClient')>();
  return { ...original, invoke: invokeMock };
});

const dashboard = {
  depotFilter: 'depot-a',
  generatedAtUtc: '2026-08-20T08:00:00Z',
} as PatchDashboardOverview;

function page(
  clientId = 'pc001.kauth.local',
  overrides: Partial<PatchClientStatePage> = {},
): PatchClientStatePage {
  return {
    items: [{
      productId: 'firefox',
      productName: 'Mozilla Firefox',
      client: {
        clientId,
        depotId: 'depot-a',
        installedVersion: '127.0',
        targetVersion: '128.0',
        installationStatus: 'installed',
        actionRequest: 'setup',
        actionResult: 'failed',
        state: 'FAILED',
      },
    }],
    total: 1,
    snapshotTotal: 1,
    page: 1,
    pageSize: 50,
    ...overrides,
  };
}

describe('PatchProductClientsTable', () => {
  beforeEach(() => {
    invokeMock.mockReset();
    invokeMock.mockResolvedValue(page());
  });

  it('owns the filtered package query while keeping selection and drill controls parent-owned', async () => {
    const onClearFilter = vi.fn();
    const onToggleClient = vi.fn();
    render(
      <PatchProductClientsTable
        connected
        dashboard={dashboard}
        productId="firefox"
        filter="installed"
        selectedClients={new Set(['pc001.kauth.local'])}
        onClearFilter={onClearFilter}
        onToggleClient={onToggleClient}
      />,
    );

    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith(
      'patchmanagement',
      'listClientStates',
      {
        depotFilter: 'depot-a',
        productId: 'firefox',
        clientSearch: null,
        productSearch: null,
        state: null,
        installationStatus: 'installed',
        page: 1,
        pageSize: 50,
        sortColumn: 'client',
        sortDirection: 'asc',
      },
    ));

    const row = (await screen.findByText('pc001')).closest('tr') as HTMLTableRowElement;
    expect(within(row).getByText('depot-a')).toBeDefined();
    expect(within(row).getByText('Failed')).toBeDefined();
    const checkbox = within(row).getByRole('checkbox', { name: 'Select client pc001' });
    expect((checkbox as HTMLInputElement).checked).toBe(true);

    await userEvent.click(checkbox);
    expect(onToggleClient).toHaveBeenCalledWith('pc001.kauth.local');
    await userEvent.click(screen.getByRole('button', { name: 'Show all clients' }));
    expect(onClearFilter).toHaveBeenCalledTimes(1);
  });

  it('paginates, changes page size and sorts with the exact server contract', async () => {
    invokeMock.mockImplementation((_module: string, action: string, payload: Record<string, unknown>) => {
      if (action !== 'listClientStates') return Promise.reject(new Error(`Unexpected action ${action}`));
      return Promise.resolve(page(`pc${String((Number(payload.page) - 1) * Number(payload.pageSize) + 1).padStart(3, '0')}.kauth.local`, {
        total: 101,
        snapshotTotal: 101,
        page: Number(payload.page),
        pageSize: Number(payload.pageSize),
      }));
    });

    render(
      <PatchProductClientsTable
        connected
        dashboard={dashboard}
        productId="firefox"
        filter={null}
        selectedClients={new Set()}
        onClearFilter={vi.fn()}
        onToggleClient={vi.fn()}
      />,
    );

    await userEvent.click(await screen.findByRole('button', { name: 'Next' }));
    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith(
      'patchmanagement',
      'listClientStates',
      expect.objectContaining({ page: 2, pageSize: 50, sortColumn: 'client', sortDirection: 'asc' }),
    ));

    await userEvent.click(screen.getByRole('button', { name: /Depot/ }));
    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith(
      'patchmanagement',
      'listClientStates',
      expect.objectContaining({ page: 1, pageSize: 50, sortColumn: 'depot', sortDirection: 'asc' }),
    ));

    await userEvent.selectOptions(screen.getByLabelText('Rows per page'), '100');
    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith(
      'patchmanagement',
      'listClientStates',
      expect.objectContaining({ page: 1, pageSize: 100, sortColumn: 'depot', sortDirection: 'asc' }),
    ));
  });

  it('keeps a failed page local and retries only its read request', async () => {
    invokeMock.mockRejectedValueOnce(new Error('Product page query failed')).mockResolvedValueOnce(page());
    render(
      <PatchProductClientsTable
        connected
        dashboard={dashboard}
        productId="firefox"
        filter={null}
        selectedClients={new Set()}
        onClearFilter={vi.fn()}
        onToggleClient={vi.fn()}
      />,
    );

    expect(await screen.findByText('The package client details could not be loaded.')).toBeDefined();
    await userEvent.click(screen.getByRole('button', { name: 'Reload package clients' }));
    expect(await screen.findByText('pc001')).toBeDefined();
    expect(invokeMock).toHaveBeenCalledTimes(2);
  });

  it('ignores a late response and resets table state for a new dashboard snapshot', async () => {
    let resolveFirst: ((value: PatchClientStatePage) => void) | undefined;
    invokeMock
      .mockImplementationOnce(() => new Promise<PatchClientStatePage>((resolve) => { resolveFirst = resolve; }))
      .mockResolvedValueOnce(page('pc-new.kauth.local'));
    const props = {
      connected: true,
      productId: 'firefox',
      filter: null,
      selectedClients: new Set<string>(),
      onClearFilter: vi.fn(),
      onToggleClient: vi.fn(),
    } as const;
    const view = render(<PatchProductClientsTable {...props} dashboard={dashboard} />);

    view.rerender(
      <PatchProductClientsTable
        {...props}
        dashboard={{ ...dashboard, generatedAtUtc: '2026-08-20T08:05:00Z' }}
      />,
    );
    expect(await screen.findByText('pc-new')).toBeDefined();
    resolveFirst?.(page('pc-old.kauth.local'));
    await waitFor(() => expect(screen.queryByText('pc-old')).toBeNull());
  });

  it('does not request live client details for the saved offline view', async () => {
    render(
      <PatchProductClientsTable
        connected={false}
        dashboard={dashboard}
        productId="firefox"
        filter={null}
        selectedClients={new Set()}
        onClearFilter={vi.fn()}
        onToggleClient={vi.fn()}
      />,
    );

    expect(screen.getByText('Client details require a live connection. The saved package data remains readable.')).toBeDefined();
    expect(screen.getByText('Client details are unavailable in the saved offline view.')).toBeDefined();
    expect(invokeMock).not.toHaveBeenCalled();
  });
});
