import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type {
  PatchClientState,
  PatchClientStatePage,
  PatchDashboardOverview,
} from '../../shared/api-types';
import { PatchClientFleetCard, formatClientName } from './PatchClientFleetCard';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../shared/bridge/bridgeClient', async (importOriginal) => {
  const original = await importOriginal<typeof import('../../shared/bridge/bridgeClient')>();
  return { ...original, invoke: invokeMock };
});

const dashboard = {
  depotFilter: 'depot-a',
  generatedAtUtc: '2026-08-20T08:00:00Z',
  summary: { clientCount: 101 },
} as PatchDashboardOverview;

const clientStates = Array.from({ length: 101 }, (_, index): PatchClientState => ({
  clientId: `pc${String(index + 1).padStart(3, '0')}.kauth.local`,
  depotId: 'depot-a',
  installedVersion: '1.0',
  targetVersion: '2.0',
  installationStatus: 'installed',
  actionRequest: 'none',
  actionResult: index % 2 === 0 ? 'failed' : 'successful',
  state: index % 2 === 0 ? 'FAILED' : 'COMPLETED',
}));

function pageFor(payload: Record<string, unknown>): PatchClientStatePage {
  let items = clientStates.map((client) => ({
    productId: client.state === 'FAILED' ? 'firefox' : '7zip',
    productName: client.state === 'FAILED' ? 'Mozilla Firefox' : '7-Zip',
    client,
  }));
  const clientSearch = String(payload.clientSearch ?? '').toLocaleLowerCase();
  const productSearch = String(payload.productSearch ?? '').toLocaleLowerCase();
  if (clientSearch) {
    items = items.filter((item) => item.client.clientId.toLocaleLowerCase().includes(clientSearch));
  }
  if (productSearch) {
    items = items.filter((item) => `${item.productId} ${item.productName}`.toLocaleLowerCase().includes(productSearch));
  }
  if (payload.state) items = items.filter((item) => item.client.state === payload.state);
  items.sort((left, right) => left.client.clientId.localeCompare(right.client.clientId));
  if (payload.sortDirection === 'desc') items.reverse();
  const page = Number(payload.page ?? 1);
  const pageSize = Number(payload.pageSize ?? 50);
  return {
    items: items.slice((page - 1) * pageSize, page * pageSize),
    total: items.length,
    snapshotTotal: clientStates.length,
    page,
    pageSize,
  };
}

describe('PatchClientFleetCard', () => {
  beforeEach(() => {
    invokeMock.mockReset();
    invokeMock.mockImplementation((_module: string, action: string, payload: Record<string, unknown>) => {
      if (action === 'listClientStates') return Promise.resolve(pageFor(payload));
      return Promise.reject(new Error(`Unexpected action ${action}`));
    });
  });

  it('owns the initial server query and remains read-only', async () => {
    render(
      <PatchClientFleetCard
        connected
        dashboard={dashboard}
      />,
    );

    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith(
      'patchmanagement',
      'listClientStates',
      {
        depotFilter: 'depot-a',
        productId: null,
        clientSearch: null,
        productSearch: null,
        state: null,
        installationStatus: null,
        page: 1,
        pageSize: 50,
        sortColumn: 'client',
        sortDirection: 'asc',
      },
    ));
    const firstRow = (await screen.findByText('pc001')).closest('tr') as HTMLTableRowElement;
    expect(within(firstRow).getByText('Failed')).toBeDefined();

    expect(within(firstRow).queryByRole('button')).toBeNull();
  });

  it('resets paging for filters and sort while preserving the exact query contract', async () => {
    render(<PatchClientFleetCard connected dashboard={dashboard} />);

    await userEvent.click(await screen.findByRole('button', { name: 'Next' }));
    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith(
      'patchmanagement',
      'listClientStates',
      expect.objectContaining({ page: 2, pageSize: 50 }),
    ));

    await userEvent.selectOptions(
      screen.getByRole('combobox', { name: 'Filter patch clients by status' }),
      'FAILED',
    );
    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith(
      'patchmanagement',
      'listClientStates',
      expect.objectContaining({ page: 1, state: 'FAILED' }),
    ));

    await userEvent.click(screen.getByRole('button', { name: /Client/ }));
    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith(
      'patchmanagement',
      'listClientStates',
      expect.objectContaining({ page: 1, sortColumn: 'client', sortDirection: 'desc' }),
    ));

    await userEvent.type(screen.getByRole('searchbox', { name: 'Filter patch packages' }), 'fire');
    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith(
      'patchmanagement',
      'listClientStates',
      expect.objectContaining({ page: 1, productSearch: 'fire', state: 'FAILED' }),
    ));
  });

  it('keeps an unavailable client page local and retries only its read request', async () => {
    invokeMock
      .mockRejectedValueOnce(new Error('Page query failed'))
      .mockImplementation((_module: string, action: string, payload: Record<string, unknown>) => {
        if (action === 'listClientStates') return Promise.resolve(pageFor(payload));
        return Promise.reject(new Error(`Unexpected action ${action}`));
      });

    render(<PatchClientFleetCard connected dashboard={dashboard} />);

    expect(await screen.findByText('The patch client list could not be loaded.')).toBeDefined();
    await userEvent.click(screen.getByRole('button', { name: 'Reload client list' }));

    expect(await screen.findByText('pc001')).toBeDefined();
    expect(invokeMock).toHaveBeenCalledTimes(2);
  });

  it('shortens only the configured client DNS suffix', () => {
    expect(formatClientName('pc1.kauth.local')).toBe('pc1');
    expect(formatClientName('PC2.KAUTH.LOCAL')).toBe('PC2');
    expect(formatClientName('pc3.example.local')).toBe('pc3.example.local');
  });
});
