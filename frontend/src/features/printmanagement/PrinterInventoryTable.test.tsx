import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { PrinterEntry } from '../../shared/api-types';
import { groupPrinters, mergePrinters } from './printers';
import { PrinterInventoryTable } from './PrinterInventoryTable';

function entry(overrides: Partial<PrinterEntry>): PrinterEntry {
  return {
    queueName: 'PK-NETPRT001',
    shareName: 'PK-NETPRT001',
    driverName: 'Kyocera KX',
    driverVersion: '8.1',
    portName: 'IP_172.20.20.31',
    deviceAddress: '172.20.20.31',
    deviceIp: '172.20.20.31',
    location: 'Accounting',
    comment: null,
    device: {
      serialNumber: 'SERIAL-1',
      model: '3509ci',
      sysName: 'PK-NETPRT001',
      sysLocation: 'Accounting',
      status: 'Idle',
      pageCount: 100,
      supplies: [{ description: 'Black', percent: 42, isLow: false }],
    },
    deviceError: null,
    deviceDataFromUtc: null,
    ...overrides,
  };
}

describe('PrinterInventoryTable', () => {
  it('owns the grouped table, semantic row presentation and expanded detail', async () => {
    const printers = mergePrinters([
      { server: 'PRSRV', entry: entry({}) },
      {
        server: 'PRSRV',
        entry: entry({
          queueName: 'PK-NETPRT002',
          shareName: null,
          deviceAddress: '172.20.20.32',
          deviceIp: '172.20.20.32',
          device: null,
          deviceError: { code: 'CONNECTION_TIMEOUT', message: 'no answer' },
        }),
      },
    ]);
    const groups = groupPrinters(printers, 'status');
    const onSort = vi.fn();
    const onToggleExpanded = vi.fn();
    const onOpenWebUi = vi.fn();

    render(
      <PrinterInventoryTable
        groups={groups}
        sort={null}
        onSort={onSort}
        notifications={{}}
        networkPolicy={null}
        dhcpReservations={{}}
        dhcpCheckedIps={new Set()}
        expanded={new Set([printers[0].key])}
        onToggleExpanded={onToggleExpanded}
        onOpenWebUi={onOpenWebUi}
      />,
    );

    expect(screen.getByRole('heading', { name: 'Idle 1 device' })).toBeDefined();
    expect(screen.getByRole('heading', { name: 'Unknown 1 device' })).toBeDefined();
    expect(screen.getByText('Queues')).toBeDefined();
    expect(screen.getByText(/\\PRSRV\\PK-NETPRT001/)).toBeDefined();

    await userEvent.click(screen.getAllByRole('columnheader', { name: /Printer/ })[0]);
    expect(onSort).toHaveBeenCalledWith('Printer');

    const unknownRow = screen.getByText('PK-NETPRT002').closest('tr')!;
    fireEvent.keyDown(unknownRow, { key: 'Enter' });
    expect(onToggleExpanded).toHaveBeenCalledWith(printers[1].key);

    await userEvent.click(screen.getByRole('button', { name: '172.20.20.31' }));
    expect(onOpenWebUi).toHaveBeenCalledWith('172.20.20.31');
  });
});
