import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { PrintServerDiff, PrintServerSnapshot } from '../../shared/api-types';
import { PrintManagementPage } from './PrintManagementPage';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../shared/bridge/bridgeClient', () => ({
  invoke: invokeMock,
  BridgeInvokeError: class extends Error {},
}));

const snapshotDenkingen: PrintServerSnapshot = {
  server: 'PRSRV-DENKINGEN',
  capturedAtUtc: '2026-07-03T12:00:00Z',
  printers: [
    {
      queueName: 'Denkingen-EG',
      shareName: 'PR-EG',
      driverName: 'Kyocera KX',
      driverVersion: '8.1.0.0',
      portName: 'IP_10.1.1.20',
      deviceAddress: '10.1.1.20',
      location: 'EG Flur',
      comment: null,
      device: {
        serialNumber: 'VCF1234567',
        model: 'UTAX P-4539i MFP',
        sysName: 'PR-EG',
        sysLocation: 'Denkingen',
        status: 'Idle',
        pageCount: 123456,
        supplies: [
          { description: 'Toner Black', percent: 8, isLow: true },
          { description: 'Toner Cyan', percent: 70, isLow: false },
        ],
      },
      deviceError: null,
    },
    {
      queueName: 'Denkingen-OG',
      shareName: null,
      driverName: 'Kyocera KX',
      driverVersion: '8.1.0.0',
      portName: 'IP_10.1.1.21',
      deviceAddress: '10.1.1.21',
      location: null,
      comment: null,
      device: null,
      deviceError: { code: 'CONNECTION_TIMEOUT', message: 'no answer' },
    },
  ],
};

const snapshotOther: PrintServerSnapshot = {
  server: 'PRSRV-ROTTWEIL',
  capturedAtUtc: '2026-07-03T12:05:00Z',
  printers: [
    {
      queueName: 'Rottweil-1',
      shareName: null,
      driverName: null,
      driverVersion: null,
      portName: null,
      deviceAddress: null,
      location: 'RW',
      comment: null,
      device: null,
      deviceError: null,
    },
  ],
};

const diff: PrintServerDiff = {
  server: 'PRSRV-DENKINGEN',
  baselineAtUtc: '2026-06-01T08:00:00Z',
  latestAtUtc: '2026-07-03T12:00:00Z',
  newDevices: [
    { serialNumber: 'NEW-1', model: 'UTAX 3207ci', queueName: 'Denkingen-EG', deviceAddress: '10.1.1.20' },
  ],
  goneDevices: [
    { serialNumber: 'OLD-9', model: 'TA 4020', queueName: 'Alt-Queue', deviceAddress: null },
  ],
  swappedQueues: [
    { queueName: 'Denkingen-OG', oldSerialNumber: 'OLD-1', newSerialNumber: 'NEW-2', oldModel: null, newModel: null },
  ],
  devicesWithoutSerialNumber: 1,
};

function mockBridge() {
  invokeMock.mockImplementation((_module: string, action: string, payload?: unknown) => {
    switch (action) {
      case 'getAppInfo':
        return Promise.resolve({ maxParallelScans: 4 });
      case 'listServers':
        return Promise.resolve({
          servers: [
            { server: 'PRSRV-DENKINGEN', capturedAtUtc: snapshotDenkingen.capturedAtUtc, snapshotCount: 3 },
            { server: 'PRSRV-ROTTWEIL', capturedAtUtc: snapshotOther.capturedAtUtc, snapshotCount: 1 },
          ],
        });
      case 'getLatest': {
        const server = (payload as { server: string }).server;
        return Promise.resolve(server === 'PRSRV-DENKINGEN' ? snapshotDenkingen : snapshotOther);
      }
      case 'getHints':
        return Promise.resolve({
          hints: [{ category: 'Default SNMP community', message: 'community is public' }],
        });
      case 'getHistory':
        return Promise.resolve({
          snapshots: [
            { id: 3, capturedAtUtc: '2026-07-03T12:00:00Z' },
            { id: 2, capturedAtUtc: '2026-06-01T08:00:00Z' },
          ],
        });
      case 'getDiff':
        return Promise.resolve(diff);
      case 'exportCsv':
        return Promise.resolve({ cancelled: false, filePath: 'C:\\temp\\printers.csv' });
      default:
        return Promise.reject(new Error(`Unexpected action ${action}`));
    }
  });
}

describe('PrintManagementPage', () => {
  beforeEach(() => {
    invokeMock.mockReset();
  });

  it('restores stored servers and shows the consolidated overview', async () => {
    mockBridge();

    render(<PrintManagementPage />);

    expect(await screen.findByText('Denkingen-EG')).toBeDefined();
    expect(screen.getByText('Rottweil-1')).toBeDefined();
    expect(screen.getByText('VCF1234567')).toBeDefined();
    expect(screen.getByText('UTAX P-4539i MFP')).toBeDefined();
    // Unreachable device renders its typed error instead of fake data
    expect(screen.getByText('CONNECTION_TIMEOUT')).toBeDefined();
    // Low toner surfaced compactly as the lowest level + low flag
    expect(screen.getByText('8% low')).toBeDefined();
    // Consistency hint surfaced
    expect(screen.getByText(/Consistency hints/)).toBeDefined();
  });

  it('filters the table by print server', async () => {
    mockBridge();

    render(<PrintManagementPage />);
    await screen.findByText('Denkingen-EG');

    await userEvent.selectOptions(screen.getByLabelText('Print server'), 'PRSRV-ROTTWEIL');

    expect(screen.queryByText('Denkingen-EG')).toBeNull();
    expect(screen.getByText('Rottweil-1')).toBeDefined();
  });

  it('marks low toner with the fail style and lists the full breakdown on expand', async () => {
    mockBridge();

    render(<PrintManagementPage />);
    const low = await screen.findByText('8% low');
    expect(low.className).toContain('fail');

    // Expanding the device row reveals every supply
    await userEvent.click(screen.getByText('Denkingen-EG'));
    expect(await screen.findByText(/Toner Black 8%/)).toBeDefined();
    expect(screen.getByText(/Toner Cyan 70%/)).toBeDefined();
  });

  it('loads the lease diff for a selected server', async () => {
    mockBridge();

    render(<PrintManagementPage />);
    await screen.findByText('Denkingen-EG');

    await userEvent.selectOptions(screen.getByLabelText('Diff server'), 'PRSRV-DENKINGEN');
    await userEvent.click(await screen.findByRole('button', { name: 'Compare' }));

    expect(await screen.findByText('NEW-1')).toBeDefined();
    expect(screen.getByText('OLD-9')).toBeDefined();
    expect(screen.getByText('OLD-1 → NEW-2')).toBeDefined();
    expect(screen.getByText(/1 device\(s\) without a readable serial/)).toBeDefined();
  });

  it('exports the CSV and reports the target path', async () => {
    mockBridge();

    render(<PrintManagementPage />);
    await screen.findByText('Denkingen-EG');

    await userEvent.click(screen.getByRole('button', { name: 'Export CSV' }));

    await waitFor(() =>
      expect(screen.getByText(/Exported to C:\\temp\\printers.csv/)).toBeDefined());
    expect(invokeMock.mock.calls.some((call) => call[1] === 'exportCsv')).toBe(true);
  });
});
