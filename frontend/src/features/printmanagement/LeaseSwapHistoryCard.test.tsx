import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { PrintServerDiff } from '../../shared/api-types';
import { LeaseSwapHistoryCard } from './LeaseSwapHistoryCard';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../shared/bridge/bridgeClient', () => ({ invoke: invokeMock }));

const diff: PrintServerDiff = {
  server: 'PRINT-A',
  baselineAtUtc: '2026-06-01T08:00:00Z',
  latestAtUtc: '2026-07-03T12:00:00Z',
  newDevices: [
    { serialNumber: 'NEW-1', model: 'Model new', queueName: 'Queue new', deviceAddress: '10.1.1.20' },
  ],
  goneDevices: [
    { serialNumber: 'OLD-9', model: 'Model old', queueName: 'Queue old', deviceAddress: null },
  ],
  swappedQueues: [
    { queueName: 'Queue swap', oldSerialNumber: 'OLD-1', newSerialNumber: 'NEW-2', oldModel: null, newModel: null },
  ],
  devicesWithoutSerialNumber: 1,
};

describe('LeaseSwapHistoryCard', () => {
  beforeEach(() => {
    invokeMock.mockReset();
    invokeMock.mockImplementation((_module: string, action: string, payload?: unknown) => {
      if (action === 'getHistory') {
        const server = (payload as { server: string }).server;
        return Promise.resolve({
          snapshots: server === 'PRINT-A'
            ? [
                { id: 3, capturedAtUtc: '2026-07-03T12:00:00Z' },
                { id: 2, capturedAtUtc: '2026-06-01T08:00:00Z' },
              ]
            : [{ id: 4, capturedAtUtc: '2026-07-04T12:00:00Z' }],
        });
      }
      if (action === 'getDiff') return Promise.resolve(diff);
      return Promise.reject(new Error(`Unexpected action ${action}`));
    });
  });

  it('loads the selected server and compares against the default previous scan', async () => {
    render(<LeaseSwapHistoryCard servers={['PRINT-A', 'PRINT-B']} />);

    await userEvent.selectOptions(screen.getByLabelText('Diff server'), 'PRINT-A');
    await userEvent.click(await screen.findByRole('button', { name: 'Compare' }));

    await waitFor(() => {
      expect(invokeMock).toHaveBeenCalledWith(
        'printmanagement',
        'getDiff',
        { server: 'PRINT-A', baselineSnapshotId: null },
      );
    });
    expect(await screen.findByText('NEW-1')).toBeDefined();
    expect(screen.getByText('OLD-9')).toBeDefined();
    expect(screen.getByText('OLD-1 → NEW-2')).toBeDefined();
    expect(screen.getByText(/1 device\(s\) without a readable serial/)).toBeDefined();
  });

  it('uses an explicit baseline and clears the prior result when the server changes', async () => {
    render(<LeaseSwapHistoryCard servers={['PRINT-A', 'PRINT-B']} />);

    await userEvent.selectOptions(screen.getByLabelText('Diff server'), 'PRINT-A');
    const baseline = await screen.findByLabelText('Baseline snapshot');
    const explicitOption = within(baseline).getAllByRole('option')[1];
    await userEvent.selectOptions(baseline, explicitOption);
    await userEvent.click(screen.getByRole('button', { name: 'Compare' }));

    await waitFor(() => {
      expect(invokeMock).toHaveBeenCalledWith(
        'printmanagement',
        'getDiff',
        { server: 'PRINT-A', baselineSnapshotId: 2 },
      );
    });
    expect(await screen.findByText('NEW-1')).toBeDefined();

    await userEvent.selectOptions(screen.getByLabelText('Diff server'), 'PRINT-B');

    await waitFor(() => {
      expect(invokeMock).toHaveBeenCalledWith('printmanagement', 'getHistory', { server: 'PRINT-B' });
    });
    expect(screen.queryByText('NEW-1')).toBeNull();
    expect(within(screen.getByLabelText('Baseline snapshot')).getAllByRole('option')).toHaveLength(1);
  });
});
