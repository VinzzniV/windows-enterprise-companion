import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { PrintServerSnapshot } from '../../shared/api-types';
import {
  PrintServerManagerCard,
  type ServerScanState,
} from './PrintServerManagerCard';

const snapshot: PrintServerSnapshot = {
  server: 'PR-SNAPSHOT',
  capturedAtUtc: '2026-08-20T01:02:03Z',
  unusedPorts: [],
  unusedDrivers: [],
  printers: [
    {
      queueName: 'Printer A',
      shareName: null,
      driverName: null,
      driverVersion: null,
      portName: null,
      deviceAddress: null,
      deviceIp: null,
      location: null,
      comment: null,
      device: null,
      deviceError: null,
      deviceDataFromUtc: null,
    },
  ],
};

const errorState: ServerScanState = {
  status: 'error',
  error: {
    message: 'The print server could not be scanned.',
    cause: 'The target rejected the sign-in attempt.',
    action: 'Check the admin credentials and retry.',
    technicalDetails: 'Code: AUTHENTICATION_FAILED',
  },
};

function renderCard(overrides: Partial<React.ComponentProps<typeof PrintServerManagerCard>> = {}) {
  const props: React.ComponentProps<typeof PrintServerManagerCard> = {
    newServer: '',
    managedServers: [],
    snapshots: {},
    scanStates: {},
    scanning: false,
    restoring: false,
    onNewServerChange: vi.fn(),
    onAddServer: vi.fn(),
    onScanServer: vi.fn(),
    onRemoveServer: vi.fn(),
    ...overrides,
  };
  render(<PrintServerManagerCard {...props} />);
  return props;
}

describe('PrintServerManagerCard', () => {
  it('withholds first-run and add controls while saved snapshots are restoring', () => {
    renderCard({ restoring: true });

    expect(screen.getByRole('status').textContent).toBe(
      'Loading saved print servers and their latest snapshots…',
    );
    expect(screen.queryByLabelText('Print server hostname')).toBeNull();
    expect(screen.queryByText(/Start with the print server/)).toBeNull();
  });

  it('owns the controlled add form and guided first-run', () => {
    const props = renderCard({ newServer: 'print-a' });
    const input = screen.getByLabelText('Print server hostname');

    expect(screen.getByText(
      'Start with the print server that owns your queues. Enter its hostname below; WEC saves it, runs the first scan, and then shows captured printers here.',
    )).toBeDefined();
    expect(screen.queryByText('No print servers yet — add one above.')).toBeNull();
    fireEvent.change(input, { target: { value: 'print-b' } });
    expect(props.onNewServerChange).toHaveBeenCalledExactlyOnceWith('print-b');
    fireEvent.submit(input.closest('form')!);
    expect(props.onAddServer).toHaveBeenCalledTimes(1);
  });

  it('presents snapshot, pending, running and failed servers with exact callbacks', async () => {
    const props = renderCard({
      managedServers: ['PR-SNAPSHOT', 'PR-PENDING', 'PR-LOADING', 'PR-ERROR'],
      snapshots: { 'PR-SNAPSHOT': snapshot },
      scanStates: {
        'PR-SNAPSHOT': { status: 'done' },
        'PR-LOADING': { status: 'loading' },
        'PR-ERROR': errorState,
      },
    });

    expect(screen.getByText(/1 printer · captured/)).toBeDefined();
    expect(screen.getByText('not scanned yet')).toBeDefined();
    expect(screen.getByText('Running')).toBeDefined();
    expect(screen.getByText('Scanning')).toBeDefined();
    expect(screen.getAllByText('The print server could not be scanned.')).toHaveLength(1);

    const snapshotRow = screen.getByText('PR-SNAPSHOT').closest('li')!;
    await userEvent.click(within(snapshotRow).getByRole('button', { name: 'Rescan' }));
    expect(props.onScanServer).toHaveBeenCalledWith('PR-SNAPSHOT');

    const errorRow = screen.getByText('PR-ERROR').closest('li')!;
    expect(within(errorRow).queryByRole('button', { name: 'Rescan' })).toBeNull();
    await userEvent.click(within(errorRow).getByRole('button', { name: 'Retry scan' }));
    expect(props.onScanServer).toHaveBeenCalledWith('PR-ERROR');
    await userEvent.click(within(errorRow).getByRole('button', { name: 'Remove' }));
    expect(props.onRemoveServer).toHaveBeenCalledExactlyOnceWith('PR-ERROR');
  });

  it('disables add, rescan and retry while a global scan is active', () => {
    renderCard({
      newServer: 'print-a',
      managedServers: ['PR-SNAPSHOT', 'PR-ERROR'],
      snapshots: { 'PR-SNAPSHOT': snapshot },
      scanStates: { 'PR-SNAPSHOT': { status: 'done' }, 'PR-ERROR': errorState },
      scanning: true,
    });

    expect((screen.getByRole('button', { name: 'Add & scan' }) as HTMLButtonElement).disabled).toBe(true);
    expect((screen.getByRole('button', { name: 'Rescan' }) as HTMLButtonElement).disabled).toBe(true);
    expect((screen.getByRole('button', { name: 'Retry scan' }) as HTMLButtonElement).disabled).toBe(true);
    for (const button of screen.getAllByRole('button', { name: 'Remove' })) {
      expect((button as HTMLButtonElement).disabled).toBe(false);
    }
  });
});
