import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { BridgeInvokeError } from '../../../shared/bridge/bridgeClient';
import { InventorySection } from './InventorySection';
import { PrintersSection } from './PrintersSection';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../../shared/bridge/bridgeClient', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../shared/bridge/bridgeClient')>()),
  invoke: invokeMock,
}));

describe('client section error presentation', () => {
  beforeEach(() => invokeMock.mockReset());

  it('keeps a WMI printer failure actionable and its mixed-language evidence collapsed', async () => {
    invokeMock.mockImplementation((_module: string, action: string) => {
      if (action === 'getLatestClientPrinters') return Promise.resolve({ scan: null });
      if (action === 'scanClientPrinters') {
        return Promise.reject(new BridgeInvokeError({
          code: 'WMI_UNAVAILABLE',
          message: 'The installed-printer query failed.',
          details: 'Die Anfrage ist ungültig.',
        }));
      }
      return Promise.resolve(null);
    });

    render(<PrintersSection target={null} />);
    await userEvent.click(await screen.findByRole('button', { name: 'Scan installed printers' }));

    expect(await screen.findByText('Installed printers could not be read.')).toBeDefined();
    expect(screen.getByText(/Windows Management Instrumentation/)).toBeDefined();
    expect(screen.getByText('Next action')).toBeDefined();
    expect(screen.getByRole('button', { name: 'Retry scan' })).toBeDefined();
    const disclosure = screen.getByText('Technical details').closest('details');
    expect(disclosure?.hasAttribute('open')).toBe(false);
    expect(disclosure?.textContent).toContain('WMI_UNAVAILABLE');
    expect(disclosure?.textContent).toContain('Die Anfrage ist ungültig.');
  });

  it('maps an inventory timeout and keeps retry inside the error state', async () => {
    invokeMock.mockImplementation((_module: string, action: string, payload?: { cacheOnly?: boolean }) => {
      if (action === 'getHardwareInfo' && payload?.cacheOnly) {
        return Promise.reject(new BridgeInvokeError({ code: 'NOT_FOUND', message: 'No cache' }));
      }
      if (action === 'getHardwareInfo') {
        return Promise.reject(new BridgeInvokeError({
          code: 'CONNECTION_TIMEOUT',
          message: 'Host PC-041 did not answer.',
        }));
      }
      return Promise.resolve(null);
    });

    render(<InventorySection target={{ host: 'PC-041' }} />);
    await userEvent.click(await screen.findByRole('button', { name: 'Run inventory scan' }));

    expect(await screen.findByText('Hardware inventory could not be captured.')).toBeDefined();
    expect(screen.getByText(/time limit/)).toBeDefined();
    expect(screen.getByRole('button', { name: 'Retry scan' })).toBeDefined();
    expect(screen.getByText('Technical details').closest('details')?.textContent)
      .toContain('Host PC-041 did not answer.');
  });
});
