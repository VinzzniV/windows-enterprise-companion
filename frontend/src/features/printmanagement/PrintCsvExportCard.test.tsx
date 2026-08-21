import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { PrintCsvExportCard } from './PrintCsvExportCard';
import type { MergedPrinter } from './printers';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../shared/bridge/bridgeClient', async (importOriginal) => {
  const original = await importOriginal<typeof import('../../shared/bridge/bridgeClient')>();
  return { ...original, invoke: invokeMock };
});

const printers: MergedPrinter[] = [
  {
    key: 'serial:VCF1234567',
    name: 'Denkingen-EG',
    site: 'DENKINGEN',
    servers: ['PRSRV-DENKINGEN'],
    queues: [{
      server: 'PRSRV-DENKINGEN',
      queueName: 'Denkingen-EG',
      shareName: 'PR-EG',
      driverName: 'Kyocera KX',
      driverVersion: '8.1.0.0',
      portName: 'IP_10.1.1.20',
      portAddress: '10.1.1.20',
    }],
    model: 'UTAX P-4539i MFP',
    serialNumber: 'VCF1234567',
    deviceAddress: '10.1.1.20',
    deviceIp: '10.1.1.20',
    location: 'Denkingen',
    deviceLocation: 'Denkingen',
    serverLocation: 'EG Flur',
    deviceAnswered: true,
    deviceDataFromUtc: null,
    status: 'Idle',
    supplies: [],
    deviceError: null,
  },
  {
    key: 'name:PRSRV-ROTTWEIL:Rottweil-1',
    name: 'Rottweil-1',
    site: 'ROTTWEIL',
    servers: ['PRSRV-ROTTWEIL'],
    queues: [{
      server: 'PRSRV-ROTTWEIL',
      queueName: 'Rottweil-1',
      shareName: null,
      driverName: null,
      driverVersion: null,
      portName: null,
      portAddress: null,
    }],
    model: null,
    serialNumber: null,
    deviceAddress: null,
    deviceIp: null,
    location: 'RW',
    deviceLocation: null,
    serverLocation: 'RW',
    deviceAnswered: false,
    deviceDataFromUtc: null,
    status: null,
    supplies: [],
    deviceError: null,
  },
];

describe('PrintCsvExportCard', () => {
  beforeEach(() => {
    invokeMock.mockReset();
    invokeMock.mockResolvedValue({ cancelled: false, filePath: 'C:\\temp\\printers.csv' });
  });

  it('owns column selection and exports exactly one row per displayed device', async () => {
    const onClose = vi.fn();
    render(<PrintCsvExportCard open printers={printers} onClose={onClose} />);

    expect(screen.getByText(/Exports the 2 displayed devices/)).toBeDefined();
    expect(screen.getByText('6 columns')).toBeDefined();
    await userEvent.click(screen.getByRole('checkbox', { name: 'Model' }));
    await userEvent.click(screen.getByRole('checkbox', { name: 'Queues' }));
    await userEvent.click(screen.getByRole('button', { name: 'Export' }));

    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith(
      'printmanagement',
      'exportCsv',
      expect.objectContaining({ csv: expect.any(String) }),
    ));
    const csv = (invokeMock.mock.calls[0][2] as { csv: string }).csv;
    expect(csv.split('\r\n')).toEqual([
      'Printer;SerialNumber;Location;IPAddress;Status;Queues',
      'Denkingen-EG;VCF1234567;Denkingen;10.1.1.20;Idle;Denkingen-EG',
      'Rottweil-1;;RW;;;Rottweil-1',
    ]);
    expect(await screen.findByText('Exported to C:\\temp\\printers.csv')).toBeDefined();
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('keeps the panel open when the save dialog is cancelled', async () => {
    invokeMock.mockResolvedValue({ cancelled: true, filePath: null });
    const onClose = vi.fn();
    render(<PrintCsvExportCard open printers={printers} onClose={onClose} />);

    await userEvent.click(screen.getByRole('button', { name: 'Export' }));

    expect(await screen.findByText('Export cancelled.')).toBeDefined();
    expect(onClose).not.toHaveBeenCalled();
    expect(screen.getByText('CSV export')).toBeDefined();
  });

  it('keeps an export failure visible without closing the workflow', async () => {
    invokeMock.mockRejectedValue(new Error('The export destination is unavailable'));
    const onClose = vi.fn();
    render(<PrintCsvExportCard open printers={printers} onClose={onClose} />);

    await userEvent.click(screen.getByRole('button', { name: 'Export' }));

    expect(await screen.findByText('The export destination is unavailable')).toBeDefined();
    expect(onClose).not.toHaveBeenCalled();
    expect(invokeMock).toHaveBeenCalledTimes(1);
  });
});
