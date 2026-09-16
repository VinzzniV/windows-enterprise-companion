import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { PrintServerDiff, PrintServerSnapshot } from '../../shared/api-types';
import { BridgeInvokeError } from '../../shared/bridge/bridgeClient';
import { PrintManagementPage } from './PrintManagementPage';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../shared/bridge/bridgeClient', () => ({
  invoke: invokeMock,
  BridgeInvokeError: class extends Error {
    constructor(readonly error: { code: string; message: string; details?: string | null }) {
      super(`${error.code}: ${error.message}`);
    }
  },
}));

const snapshotDenkingen: PrintServerSnapshot = {
  server: 'PRSRV-DENKINGEN',
  capturedAtUtc: '2026-07-03T12:00:00Z',
  unusedPorts: [{ name: 'IP_10.1.1.99', hostAddress: '10.1.1.99' }],
  unusedDrivers: [],
  printers: [
    {
      queueName: 'Denkingen-EG',
      shareName: 'PR-EG',
      driverName: 'Kyocera KX',
      driverVersion: '8.1.0.0',
      portName: 'IP_10.1.1.20',
      deviceAddress: '10.1.1.20',
      deviceIp: '10.1.1.20',
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
      deviceDataFromUtc: null,
    },
    {
      queueName: 'Denkingen-OG',
      shareName: null,
      driverName: 'Kyocera KX',
      driverVersion: '8.1.0.0',
      portName: 'IP_10.1.1.21',
      deviceAddress: '10.1.1.21',
      deviceIp: '10.1.1.21',
      location: null,
      comment: null,
      device: null,
      deviceError: { code: 'CONNECTION_TIMEOUT', message: 'no answer' },
      deviceDataFromUtc: null,
    },
  ],
};

const snapshotOther: PrintServerSnapshot = {
  server: 'PRSRV-ROTTWEIL',
  capturedAtUtc: '2026-07-03T12:05:00Z',
  unusedPorts: [],
  unusedDrivers: [],
  printers: [
    {
      queueName: 'Rottweil-1',
      shareName: null,
      driverName: null,
      driverVersion: null,
      portName: null,
      deviceAddress: null,
      deviceIp: null,
      location: 'RW',
      comment: null,
      device: null,
      deviceError: null,
      deviceDataFromUtc: null,
    },
  ],
};

const snapshotEmpty: PrintServerSnapshot = {
  server: 'PRSRV-EMPTY',
  capturedAtUtc: '2026-08-20T03:00:00Z',
  unusedPorts: [],
  unusedDrivers: [],
  printers: [],
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
      case 'getNetworkPolicy':
        return Promise.resolve({
          printerSubnets: ['172.20.20.0/24'],
          legacySubnets: ['10.1.1.0/24'],
          dhcpServer: 'dc01',
        });
      case 'checkDhcp':
        return Promise.resolve({ reserved: [{ ip: '10.1.1.20', mac: '00-11-22', name: 'PR-EG' }] });
      case 'probeHosts':
        return Promise.resolve({ results: [{ host: '10.1.1.99', reachable: false, latencyMs: null }] });
      case 'checkNotificationConfig': {
        const host = (payload as { host: string }).host;
        return Promise.resolve(host === '10.1.1.20'
          ? { host, status: 'OK', rules: [], error: null }
          : {
              host,
              status: 'WARNING',
              rules: [{ id: 'smtp', title: 'SMTP enabled', passed: false, detail: 'Disabled' }],
              error: null,
            });
      }
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

function mockEmptyBridge(snapshot: PrintServerSnapshot | null = null) {
  invokeMock.mockImplementation((_module: string, action: string) => {
    switch (action) {
      case 'getAppInfo':
        return Promise.resolve({ maxParallelScans: 4 });
      case 'listServers':
        return Promise.resolve({
          servers: snapshot
            ? [{ server: snapshot.server, capturedAtUtc: snapshot.capturedAtUtc, snapshotCount: 1 }]
            : [],
        });
      case 'getLatest':
        return snapshot ? Promise.resolve(snapshot) : Promise.reject(new Error('No stored snapshot'));
      case 'getHints':
        return Promise.resolve({ hints: [] });
      case 'getNetworkPolicy':
        return Promise.resolve({ printerSubnets: [], legacySubnets: [], dhcpServer: null });
      default:
        return Promise.reject(new Error(`Unexpected action ${action}`));
    }
  });
}

describe('PrintManagementPage', () => {
  beforeEach(() => {
    invokeMock.mockReset();
  });

  it('shows one guided first-run instead of repeating server and printer empty states', async () => {
    mockEmptyBridge();

    render(<PrintManagementPage />);

    expect(await screen.findByText(
      'Start with the print server that owns your queues. Enter its hostname below; WEC saves it, runs the first scan, and then shows captured printers here.',
    )).toBeDefined();
    expect(screen.getByLabelText('Print server hostname')).toBeDefined();
    expect(screen.getByRole('button', { name: 'Add & scan' })).toBeDefined();
    expect(screen.queryByText('No print servers yet — add one above.')).toBeNull();
    expect(screen.queryByText('No printers captured yet')).toBeNull();
  });

  it('guides an existing empty snapshot back to its saved server instead of adding another one', async () => {
    mockEmptyBridge(snapshotEmpty);

    render(<PrintManagementPage />);

    expect(await screen.findByText(/0 printers · captured/)).toBeDefined();
    expect(screen.getByText('No printers captured yet')).toBeDefined();
    expect(screen.getByText(
      'No saved scan currently contains printer queues. Rescan a server above; captured printers will appear here automatically.',
    )).toBeDefined();
    expect(screen.queryByText(/Add a print server above/)).toBeNull();
  });

  it('restores stored servers and shows the consolidated overview', async () => {
    mockBridge();

    render(<PrintManagementPage />);

    expect(await screen.findByText('Denkingen-EG')).toBeDefined();
    expect(screen.getByText('Rottweil-1')).toBeDefined();
    expect(screen.getByText('VCF1234567')).toBeDefined();
    expect(screen.getByText('UTAX P-4539i MFP')).toBeDefined();
    // A failed observation is unknown availability; the provider code is detail, not the primary status.
    const unreachableRow = screen.getByText('Denkingen-OG').closest('tr')!;
    expect(within(unreachableRow).getAllByText('Unknown').length).toBeGreaterThan(0);
    expect(within(unreachableRow).getByText('No response')).toBeDefined();
    expect(screen.getByTitle('CONNECTION_TIMEOUT')).toBeDefined();
    expect(screen.queryByText('CONNECTION_TIMEOUT')).toBeNull();
    // Low toner surfaced compactly as the lowest level + low flag
    expect(screen.getByText('8% low')).toBeDefined();
    // Consistency hint surfaced
    expect(screen.getByText(/Consistency hints/)).toBeDefined();
  });

  it('shows the device location as truth and flags the print server mismatch', async () => {
    mockBridge();

    render(<PrintManagementPage />);
    await screen.findByText('Denkingen-EG');

    // SNMP sysLocation ('Denkingen') is the truth, not the print server label ('EG Flur')
    expect(screen.getByText('Denkingen')).toBeDefined();
    expect(screen.getByText(/Print server: EG Flur/)).toBeDefined();
  });

  it('classifies the subnet and checks DHCP reservations on demand', async () => {
    mockBridge();

    render(<PrintManagementPage />);
    await screen.findByText('Denkingen-EG');

    // 10.1.1.x is configured as a legacy subnet → a pending lifecycle state.
    const reservedRow = (await screen.findByText('Denkingen-EG')).closest('tr')!;
    const unreservedRow = screen.getByText('Denkingen-OG').closest('tr')!;
    expect(within(reservedRow).getByText('Pending')).toBeDefined();
    expect(within(reservedRow).getByText('Legacy subnet')).toBeDefined();

    await userEvent.click(screen.getByRole('button', { name: /Check DHCP reservations/ }));

    // 10.1.1.20 is available as a reservation; 10.1.1.21 is explicitly missing one.
    expect(await within(reservedRow).findByText('Available')).toBeDefined();
    expect(within(reservedRow).getByText('Reserved')).toBeDefined();
    expect(within(unreservedRow).getByText('Missing')).toBeDefined();
    expect(within(unreservedRow).getByText('No reservation')).toBeDefined();
  });

  it('does not mark printers outside a filtered DHCP request as missing', async () => {
    mockBridge();
    const user = userEvent.setup();

    render(<PrintManagementPage />);
    await screen.findByText('Denkingen-EG');

    const search = screen.getByLabelText('Search printers');
    await user.click(search);
    await user.paste('Denkingen-EG');
    await user.click(screen.getByRole('button', { name: /Check DHCP reservations/ }));

    expect(await screen.findByText(
      '1 of 2 printer IPs checked; all checked printers have reservations.',
    )).toBeDefined();
    const dhcpCall = invokeMock.mock.calls.find((call) => call[1] === 'checkDhcp');
    expect((dhcpCall?.[2] as { ips: string[] }).ips).toEqual(['10.1.1.20']);

    await user.clear(search);
    const checkedRow = (await screen.findByText('Denkingen-EG')).closest('tr')!;
    const uncheckedRow = screen.getByText('Denkingen-OG').closest('tr')!;
    expect(within(checkedRow).getByText('Available')).toBeDefined();
    expect(within(uncheckedRow).queryByText('Missing')).toBeNull();
    expect(within(uncheckedRow).queryByText('No reservation')).toBeNull();

    await user.clear(screen.getByLabelText('DHCP server'));
    expect(within(checkedRow).queryByText('Available')).toBeNull();
    expect(screen.queryByText(/Checked on dc01/)).toBeNull();
  });

  it('keeps the unused-port workflow in the English product language', async () => {
    mockBridge();
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false);

    render(<PrintManagementPage />);
    await screen.findByText('Denkingen-EG');

    expect(screen.getByText('Unused ports (1)')).toBeDefined();
    expect(screen.getByText(/TCP\/IP ports that are no longer used by any printer/)).toBeDefined();
    expect(screen.getByRole('checkbox', { name: 'Select port IP_10.1.1.99' })).toBeDefined();

    await userEvent.click(screen.getByRole('button', { name: 'Check reachability' }));
    const portRow = screen.getByRole('checkbox', { name: 'Select port IP_10.1.1.99' }).closest('tr')!;
    expect(await within(portRow).findByText('Unknown')).toBeDefined();
    expect(within(portRow).getByText('No response')).toBeDefined();

    await userEvent.click(screen.getByRole('checkbox', { name: 'Select port IP_10.1.1.99' }));
    await userEvent.click(screen.getByRole('button', { name: 'Delete selected (1)' }));
    expect(confirm).toHaveBeenCalledWith(expect.stringContaining('Permanently delete 1 unused port'));
    expect(invokeMock.mock.calls.some((call) => call[1] === 'deleteUnusedPorts')).toBe(false);
    confirm.mockRestore();
  });

  it('separates notification health from its check context', async () => {
    mockBridge();

    render(<PrintManagementPage />);
    const configuredRow = (await screen.findByText('Denkingen-EG')).closest('tr')!;
    const warningRow = screen.getByText('Denkingen-OG').closest('tr')!;

    await userEvent.click(screen.getByRole('button', { name: 'Check notifications' }));

    expect(await within(configuredRow).findByText('Healthy')).toBeDefined();
    expect(within(configuredRow).getByText('Configured')).toBeDefined();
    expect(await within(warningRow).findByText('Warning')).toBeDefined();
    expect(within(warningRow).getByText('1 issue')).toBeDefined();
  });

  it('shows an active print-server scan as running execution', async () => {
    mockBridge();
    const defaultImplementation = invokeMock.getMockImplementation()!;
    let resolveScan!: (snapshot: PrintServerSnapshot) => void;
    const scan = new Promise<PrintServerSnapshot>((resolve) => { resolveScan = resolve; });
    invokeMock.mockImplementation((module: string, action: string, payload?: unknown) =>
      action === 'scanServer' ? scan : defaultImplementation(module, action, payload));

    render(<PrintManagementPage />);
    const server = (await screen.findAllByText('PRSRV-DENKINGEN'))
      .find((element) => element.closest('li'))!;
    await userEvent.click(server.closest('li')!.querySelector('button')!);

    expect(await screen.findByText('Running')).toBeDefined();
    expect(screen.getByText('Scanning')).toBeDefined();

    resolveScan(snapshotDenkingen);
    await waitFor(() => expect(screen.queryByText('Scanning')).toBeNull());
  });

  it('filters the table by print server', async () => {
    mockBridge();

    render(<PrintManagementPage />);
    await screen.findByText('Denkingen-EG');

    await userEvent.selectOptions(screen.getByLabelText('Print server'), 'PRSRV-ROTTWEIL');

    expect(screen.queryByText('Denkingen-EG')).toBeNull();
    expect(screen.getByText('Rottweil-1')).toBeDefined();
  });

  it('groups printers by canonical semantic status instead of provider values', async () => {
    mockBridge();

    render(<PrintManagementPage />);
    await screen.findByText('Denkingen-EG');

    await userEvent.selectOptions(screen.getByLabelText('Group printers by'), 'status');

    expect(screen.getByRole('heading', { name: 'Idle 1 device' })).toBeDefined();
    expect(screen.getByRole('heading', { name: 'Unknown 2 devices' })).toBeDefined();
    expect(screen.queryByRole('heading', { name: /Not answering/ })).toBeNull();
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

  it('expands a device row from the keyboard', async () => {
    mockBridge();

    render(<PrintManagementPage />);
    const row = (await screen.findByText('Denkingen-EG')).closest('tr')!;
    expect(row.getAttribute('role')).toBe('button');
    fireEvent.keyDown(row, { key: 'Enter' });

    expect(await screen.findByText(/Toner Black 8%/)).toBeDefined();
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

  it('shows a failed server scan once with admin guidance, details and retry', async () => {
    mockBridge();
    const defaultImplementation = invokeMock.getMockImplementation()!;
    invokeMock.mockImplementation((module: string, action: string, payload?: unknown) =>
      action === 'scanServer'
        ? Promise.reject(new BridgeInvokeError({
            code: 'AUTHENTICATION_FAILED',
            message: 'The remote logon failed.',
            details: 'Kerberos returned 0x52e.',
          }))
        : defaultImplementation(module, action, payload),
    );

    render(<PrintManagementPage />);
    const server = (await screen.findAllByText('PRSRV-DENKINGEN'))
      .find((element) => element.closest('li'))!;
    await userEvent.click(server.closest('li')!.querySelector('button')!);

    expect(await screen.findByText('The print server could not be scanned.')).toBeDefined();
    expect(screen.getAllByText('The print server could not be scanned.')).toHaveLength(1);
    expect(screen.getByText('Next action')).toBeDefined();
    expect(screen.getByText('Technical details')).toBeDefined();
    expect(screen.getByRole('button', { name: 'Retry scan' })).toBeDefined();
    expect(screen.queryByText('Scan failed')).toBeNull();
  });

  it('exports the picked columns as one row per device', async () => {
    mockBridge();

    render(<PrintManagementPage />);
    await screen.findByText('Denkingen-EG');

    await userEvent.click(screen.getByRole('button', { name: 'Export CSV' }));
    // Model is on by default, Queues is not — flip both
    await userEvent.click(screen.getByRole('checkbox', { name: 'Model' }));
    await userEvent.click(screen.getByRole('checkbox', { name: 'Queues' }));
    await userEvent.click(screen.getByRole('button', { name: 'Export' }));

    await waitFor(() =>
      expect(screen.getByText(/Exported to C:\\temp\\printers.csv/)).toBeDefined());
    const call = invokeMock.mock.calls.find((call) => call[1] === 'exportCsv');
    expect(call).toBeDefined();
    const csv = (call![2] as { csv: string }).csv;
    const lines = csv.split('\r\n');
    expect(lines[0]).toBe('Printer;SerialNumber;Location;IPAddress;Status;Queues');
    // One row per physical device (3 queues across both servers = 3 devices here)
    expect(lines).toHaveLength(4);
    expect(lines[1]).toBe('Denkingen-EG;VCF1234567;Denkingen;10.1.1.20;Idle;Denkingen-EG');
  });
});
