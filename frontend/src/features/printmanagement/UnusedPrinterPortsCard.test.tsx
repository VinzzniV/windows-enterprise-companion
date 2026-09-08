import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { TargetRequest } from '../../shared/api-types';
import { UnusedPrinterPortsCard, type UnusedPrinterPortRow } from './UnusedPrinterPortsCard';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../shared/bridge/bridgeClient', async (importOriginal) => {
  const original = await importOriginal<typeof import('../../shared/bridge/bridgeClient')>();
  return { ...original, invoke: invokeMock };
});

const ports: UnusedPrinterPortRow[] = [
  { server: 'print-a', port: { name: 'IP_A', hostAddress: '10.0.0.1' } },
  { server: 'print-a', port: { name: 'IP_B', hostAddress: '10.0.0.2' } },
  { server: 'print-b', port: { name: 'IP_C', hostAddress: '10.0.0.1' } },
];

const toServerRequest = (host: string): TargetRequest => ({
  host,
  userName: 'admin',
  domain: null,
  password: 'session-secret',
});

describe('UnusedPrinterPortsCard', () => {
  beforeEach(() => {
    invokeMock.mockReset();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('owns selection and deduplicated reachability evidence for every port row', async () => {
    invokeMock.mockResolvedValue({
      results: [
        { host: '10.0.0.1', reachable: true, latencyMs: 2 },
        { host: '10.0.0.2', reachable: false, latencyMs: null },
      ],
    });
    render(
      <UnusedPrinterPortsCard
        ports={ports}
        adminAvailable={false}
        toServerRequest={toServerRequest}
        onRefreshServers={vi.fn()}
      />,
    );

    expect(screen.getByText('Unused ports (3)')).toBeDefined();
    expect(screen.getByText(/Set remote account at the top right/)).toBeDefined();
    await userEvent.click(screen.getByRole('button', { name: 'Select all' }));
    expect(screen.getByRole('button', { name: 'Delete selected (3)' })).toBeDefined();
    await userEvent.click(screen.getByRole('button', { name: 'Check reachability' }));

    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith(
      'connectivity',
      'probeHosts',
      { hosts: ['10.0.0.1', '10.0.0.2'] },
      120_000,
    ));
    const reachableRow = screen.getByRole('checkbox', { name: 'Select port IP_A' }).closest('tr')!;
    const duplicateRow = screen.getByRole('checkbox', { name: 'Select port IP_C' }).closest('tr')!;
    const unknownRow = screen.getByRole('checkbox', { name: 'Select port IP_B' }).closest('tr')!;
    expect(within(reachableRow).getByText('Available')).toBeDefined();
    expect(within(reachableRow).getByText('Reachable')).toBeDefined();
    expect(within(duplicateRow).getByText('Available')).toBeDefined();
    expect(within(unknownRow).getByText('Unknown')).toBeDefined();
    expect(within(unknownRow).getByText('No response')).toBeDefined();
  });

  it('stops before every mutation when the operator cancels confirmation', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false);
    const onRefreshServers = vi.fn();
    render(
      <UnusedPrinterPortsCard
        ports={ports}
        adminAvailable
        toServerRequest={toServerRequest}
        onRefreshServers={onRefreshServers}
      />,
    );

    await userEvent.click(screen.getByRole('button', { name: 'Select all' }));
    await userEvent.click(screen.getByRole('button', { name: 'Delete selected (3)' }));

    expect(confirm).toHaveBeenCalledWith(expect.stringContaining(
      'Permanently delete 3 unused ports on 2 servers?',
    ));
    expect(invokeMock).not.toHaveBeenCalled();
    expect(onRefreshServers).not.toHaveBeenCalled();
  });

  it('groups a confirmed delete by server and aggregates every outcome before refresh', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    invokeMock.mockImplementation((_module: string, action: string, payload: { target: TargetRequest }) => {
      if (action !== 'deleteUnusedPorts') return Promise.reject(new Error(`Unexpected ${action}`));
      if (payload.target.host === 'print-a') {
        return Promise.resolve({
          results: [
            { name: 'IP_A', removed: true, error: null },
            { name: 'IP_B', removed: false, error: 'still in use' },
          ],
        });
      }
      return Promise.reject(new Error('print-b unavailable'));
    });
    const onRefreshServers = vi.fn();
    render(
      <UnusedPrinterPortsCard
        ports={ports}
        adminAvailable
        toServerRequest={toServerRequest}
        onRefreshServers={onRefreshServers}
      />,
    );

    await userEvent.click(screen.getByRole('button', { name: 'Select all' }));
    await userEvent.click(screen.getByRole('button', { name: 'Delete selected (3)' }));

    await waitFor(() => expect(invokeMock).toHaveBeenCalledTimes(2));
    expect(invokeMock).toHaveBeenCalledWith(
      'printmanagement',
      'deleteUnusedPorts',
      { target: toServerRequest('print-a'), portNames: ['IP_A', 'IP_B'], confirmed: true },
      120_000,
    );
    expect(invokeMock).toHaveBeenCalledWith(
      'printmanagement',
      'deleteUnusedPorts',
      { target: toServerRequest('print-b'), portNames: ['IP_C'], confirmed: true },
      120_000,
    );
    expect(await screen.findByText(
      '1 port removed · 1 refused (still in use) · 1 server error (print-b unavailable)',
    )).toBeDefined();
    expect(onRefreshServers).toHaveBeenCalledWith(['print-a', 'print-b']);
    expect(screen.getByRole('button', { name: 'Delete selected (0)' }).hasAttribute('disabled'))
      .toBe(true);
  });
});
