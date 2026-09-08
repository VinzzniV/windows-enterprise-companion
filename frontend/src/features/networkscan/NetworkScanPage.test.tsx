import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { NetworkScanPage } from './NetworkScanPage';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../shared/bridge/bridgeClient', () => ({
  invoke: invokeMock,
  BridgeInvokeError: class extends Error {},
  BridgeCancelledError: class extends Error {},
  BridgeTimeoutError: class extends Error {},
  BridgeUnavailableError: class extends Error {},
}));

describe('NetworkScanPage', () => {
  beforeEach(() => invokeMock.mockReset());

  it('keeps scan diagnostics behind actionable guidance and offers a local retry', async () => {
    invokeMock
      .mockRejectedValueOnce(new Error('raw nmap provider failure'))
      .mockResolvedValue({ hosts: [], target: '172.20.20.0/24', dhcpChecked: false });

    render(<NetworkScanPage />);
    await userEvent.click(screen.getByRole('button', { name: 'Scan' }));

    const alert = await screen.findByRole('alert');
    expect(within(alert).getByText('The network scan could not be completed.')).toBeDefined();
    expect(within(alert).getByText('Cause')).toBeDefined();
    expect(within(alert).getByText('Next action')).toBeDefined();
    expect(within(alert).getByRole('button', { name: 'Scan again' })).toBeDefined();
    const details = within(alert).getByText('Technical details').closest('details') as HTMLDetailsElement;
    expect(details.open).toBe(false);
    await userEvent.click(details.querySelector('summary')!);
    expect(within(alert).getByText(/raw nmap provider failure/)).toBeDefined();
  });

  it('uses the product language for the complete scan workflow and result vocabulary', async () => {
    invokeMock.mockResolvedValue({
      target: '172.20.20.0/24',
      portsScanned: true,
      dhcpChecked: true,
      hosts: [
        {
          ip: '172.20.20.10', hostname: 'printer-01', isUp: true, kind: 'PRINTER', openPorts: [80],
          macAddress: null, macVendor: 'Vendor A', hasReservation: true, reservationName: 'printer-01',
        },
        {
          ip: '172.20.20.11', hostname: 'switch-01', isUp: true, kind: 'NETWORK_DEVICE', openPorts: [22],
          macAddress: null, macVendor: 'Vendor B', hasReservation: false, reservationName: null,
        },
        {
          ip: '172.20.20.12', hostname: 'client-01', isUp: false, kind: 'COMPUTER', openPorts: [],
          macAddress: null, macVendor: null, hasReservation: true, reservationName: 'client-01',
        },
        {
          ip: '172.20.20.13', hostname: null, isUp: true, kind: 'UNKNOWN', openPorts: [],
          macAddress: null, macVendor: null, hasReservation: true, reservationName: 'unknown-01',
        },
      ],
    });

    render(<NetworkScanPage />);

    expect(screen.getByRole('heading', { name: 'Network Scan' })).toBeDefined();
    expect(screen.getByText('Target (CIDR, range, or IP)')).toBeDefined();
    expect(screen.getByText('DHCP server (optional)')).toBeDefined();
    expect(screen.getByText('Scan ports')).toBeDefined();

    await userEvent.click(screen.getByRole('button', { name: 'Scan' }));

    expect(await screen.findByText('Result for 172.20.20.0/24')).toBeDefined();
    for (const heading of ['Hostname', 'Type', 'Status', 'Open ports', 'MAC vendor']) {
      expect(screen.getByRole('columnheader', { name: heading })).toBeDefined();
    }
    for (const label of ['Printer', 'Network device', 'Computer', 'Unknown']) {
      expect(screen.getByText(label)).toBeDefined();
    }
    expect(screen.getAllByText('Available')).toHaveLength(2);
    expect(screen.getAllByText('Reserved')).toHaveLength(2);
    expect(screen.getByText('Missing')).toBeDefined();
    expect(screen.getByText('No reservation')).toBeDefined();
    expect(screen.getByText('Stale')).toBeDefined();
    expect(screen.getByText('Reservation did not answer')).toBeDefined();
    expect(screen.getByText('Without reservation')).toBeDefined();
    expect(screen.getByText('Stale reservations')).toBeDefined();
  });

  it('separates active reachability from unchecked DHCP context', async () => {
    invokeMock.mockResolvedValue({
      target: '172.20.20.10',
      portsScanned: false,
      dhcpChecked: false,
      hosts: [{
        ip: '172.20.20.10', hostname: 'client-01', isUp: true, kind: 'COMPUTER', openPorts: [],
        macAddress: null, macVendor: null, hasReservation: false, reservationName: null,
      }],
    });

    render(<NetworkScanPage />);
    await userEvent.click(screen.getByRole('button', { name: 'Scan' }));

    const row = await screen.findByRole('row', { name: /client-01/ });
    expect(within(row).getByText('Available')).toBeDefined();
    expect(within(row).getByText('Active')).toBeDefined();
    expect(within(row).queryByText('Reserved')).toBeNull();
  });

  it('keeps local validation and DHCP session guidance in the product language', async () => {
    render(<NetworkScanPage />);

    const target = screen.getByRole('textbox', { name: 'Target (CIDR, range, or IP)' });
    await userEvent.clear(target);
    await userEvent.click(screen.getByRole('button', { name: 'Scan' }));

    const alert = screen.getByRole('alert');
    expect(within(alert).getByText('The network scan requires a target.')).toBeDefined();
    expect(within(alert).getByText('The CIDR, IP range, or individual IP field is empty.')).toBeDefined();
    expect(invokeMock).not.toHaveBeenCalled();

    await userEvent.type(screen.getByRole('textbox', { name: 'DHCP server (optional)' }), 'PK-SRVDC001');
    expect(screen.getByText(/For the DHCP check, use “Set remote account”/)).toBeDefined();
  });
});
