import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { TargetProvider } from '../../shared/targets/TargetContext';
import { ClientBulkActions } from './ClientBulkActions';

const { invokeMock, cancelMock } = vi.hoisted(() => ({
  invokeMock: vi.fn(),
  cancelMock: vi.fn(),
}));

vi.mock('../../shared/bridge/bridgeClient', () => ({
  invoke: invokeMock,
  invokeCancellable: (module: string, action: string, payload: unknown) => ({
    requestId: 'batch-request',
    promise: invokeMock(module, action, payload),
    cancel: cancelMock,
  }),
  subscribe: vi.fn(() => () => {}),
  BridgeCancelledError: class extends Error {},
  BridgeInvokeError: class extends Error {},
  BridgeTimeoutError: class extends Error {},
  BridgeUnavailableError: class extends Error {},
}));

function renderActions() {
  return render(
    <MemoryRouter>
      <TargetProvider>
        <ClientBulkActions
          selectedHosts={['pc-01.corp.local', 'pc-02.corp.local']}
          maxBatchHosts={50}
          onRunningChange={vi.fn()}
          onCompleted={vi.fn()}
        />
      </TargetProvider>
    </MemoryRouter>,
  );
}

describe('ClientBulkActions', () => {
  beforeEach(() => {
    invokeMock.mockReset();
    cancelMock.mockReset();
    invokeMock.mockImplementation((module: string, action: string) => {
      if (module === 'targets' && action === 'list') return Promise.resolve({ targets: [] });
      return new Promise(() => {});
    });
  });

  it('does not start from selection alone and cancels the explicit bridge request', async () => {
    renderActions();

    expect(screen.getByText('2 of 50 hosts selected')).toBeTruthy();
    const options = screen.getByText('Batch options and selected hosts').closest('details') as HTMLDetailsElement;
    expect(options.open).toBe(true);
    expect(invokeMock).not.toHaveBeenCalledWith('inventory', 'runBatchScan', expect.anything());
    await userEvent.click(screen.getByRole('button', { name: 'Run Inventory' }));

    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith('inventory', 'runBatchScan', {
      hosts: ['pc-01.corp.local', 'pc-02.corp.local'],
    }));
    expect(options.open).toBe(false);
    expect(screen.getByText('Inventory progress')).toBeTruthy();
    await userEvent.click(screen.getByRole('button', { name: 'Cancel batch' }));
    expect(cancelMock).toHaveBeenCalledTimes(1);
  });

  it('requires a configured host bound before enabling execution', () => {
    render(
      <MemoryRouter>
        <ClientBulkActions
          selectedHosts={['pc-01.corp.local']}
          maxBatchHosts={null}
          onRunningChange={vi.fn()}
          onCompleted={vi.fn()}
        />
      </MemoryRouter>,
    );

    expect(screen.getByRole('button', { name: 'Run Inventory' }).hasAttribute('disabled')).toBe(true);
    expect(screen.getByText(/configured batch limit is unavailable/)).toBeTruthy();
  });

  it('links Security batch results to the matching Client 360 section', async () => {
    invokeMock.mockImplementation((module: string, action: string) => {
      if (module === 'targets' && action === 'list') return Promise.resolve({ targets: [] });
      if (module === 'security' && action === 'runBatchScan') return Promise.resolve({
        startedAtUtc: '2026-08-27T08:00:00Z',
        completedAtUtc: '2026-08-27T08:01:00Z',
        hosts: [{
          host: 'pc-01.corp.local',
          status: 'FAILED',
          scan: null,
          error: {
            host: 'pc-01.corp.local',
            phase: 'CONNECT',
            code: 'WIN_RM_UNAVAILABLE',
            message: 'WinRM did not answer.',
            details: null,
          },
        }],
      });
      return new Promise(() => {});
    });

    renderActions();
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Bulk scan operation' }), 'security');
    await userEvent.click(screen.getByRole('button', { name: 'Run Security' }));

    const hostLink = await screen.findByRole('link', { name: 'pc-01.corp.local' });
    expect(hostLink.getAttribute('href')).toBe('/clients/pc-01.corp.local?section=security');
  });
});
