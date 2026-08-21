import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { RolloutPreview } from '../../shared/api-types';
import { PatchDeploymentWorkflow } from './PatchDeploymentWorkflow';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../shared/bridge/bridgeClient', async (importOriginal) => {
  const original = await importOriginal<typeof import('../../shared/bridge/bridgeClient')>();
  return { ...original, invoke: invokeMock };
});

function preview(clientId = 'pc1.kauth.local'): RolloutPreview {
  return {
    productId: 'firefox',
    productName: 'Mozilla Firefox',
    depotFilter: 'depot-a',
    plannedAction: 'setup',
    clients: [{
      clientId,
      depotId: 'depot-a',
      installedVersion: '127.0',
      targetVersion: '128.0',
      currentState: 'UPDATE_AVAILABLE',
    }],
    generatedAtUtc: '2026-08-20T08:00:00Z',
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, resolve, reject };
}

const baseProps = {
  connected: true,
  productId: 'firefox',
  depotFilter: 'depot-a',
  selectedClients: new Set<string>(),
  onDashboardRefresh: vi.fn(),
};

describe('PatchDeploymentWorkflow', () => {
  beforeEach(() => {
    invokeMock.mockReset();
    baseProps.onDashboardRefresh.mockReset();
  });

  it('loads the exact selected-client preview and presents its workflow evidence', async () => {
    const pending = deferred<RolloutPreview>();
    invokeMock.mockReturnValue(pending.promise);
    render(
      <PatchDeploymentWorkflow
        {...baseProps}
        selectedClients={new Set(['pc1.kauth.local', 'pc2.kauth.local'])}
      />,
    );

    await userEvent.click(screen.getByRole('button', { name: 'Prepare deployment (2 selected)' }));
    expect(screen.getByRole('button', { name: 'Preparing deployment…' }).hasAttribute('disabled')).toBe(true);
    expect(invokeMock).toHaveBeenCalledWith('patchmanagement', 'getRolloutPreview', {
      productId: 'firefox',
      depotFilter: 'depot-a',
      clientIds: ['pc1.kauth.local', 'pc2.kauth.local'],
    });

    pending.resolve(preview());
    expect(await screen.findByText(/Nothing has been sent to opsi yet/)).toBeDefined();
    const row = screen.getByText('pc1').closest('tr') as HTMLTableRowElement;
    expect(within(row).getByText('depot-a')).toBeDefined();
    expect(within(row).getByText('Update available')).toBeDefined();
  });

  it('uses the default affected-client scope when no clients are selected', async () => {
    invokeMock.mockResolvedValue(preview());
    render(<PatchDeploymentWorkflow {...baseProps} />);

    await userEvent.click(screen.getByRole('button', { name: 'Prepare deployment (outdated and failed clients)' }));
    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith(
      'patchmanagement',
      'getRolloutPreview',
      expect.objectContaining({ clientIds: null }),
    ));
  });

  it('requires explicit confirmation before requesting rollout and refreshes after success', async () => {
    invokeMock.mockImplementation((_module: string, action: string) => {
      if (action === 'getRolloutPreview') return Promise.resolve(preview());
      if (action === 'requestRollout') return Promise.resolve({ requestedClientCount: 1 });
      return Promise.reject(new Error(`Unexpected action ${action}`));
    });
    const onDashboardRefresh = vi.fn();
    render(<PatchDeploymentWorkflow {...baseProps} onDashboardRefresh={onDashboardRefresh} />);

    await userEvent.click(screen.getByRole('button', { name: /Prepare deployment/ }));
    const requestButton = await screen.findByRole('button', { name: 'Request deployment for 1 client(s)' });
    expect(requestButton.hasAttribute('disabled')).toBe(true);
    expect(invokeMock.mock.calls.some((call) => call[1] === 'requestRollout')).toBe(false);

    await userEvent.click(screen.getByRole('checkbox', { name: /I have reviewed the affected clients/ }));
    await userEvent.click(requestButton);
    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith(
      'patchmanagement',
      'requestRollout',
      {
        productId: 'firefox',
        clientIds: ['pc1.kauth.local'],
        depotFilter: 'depot-a',
        confirmed: true,
      },
    ));
    expect(await screen.findByText(/Deployment requested for 1 client/)).toBeDefined();
    expect(onDashboardRefresh).toHaveBeenCalledTimes(1);
  });

  it('keeps preview failures local and allows a fresh preview attempt', async () => {
    invokeMock.mockRejectedValueOnce(new Error('Preview unavailable')).mockResolvedValueOnce(preview());
    render(<PatchDeploymentWorkflow {...baseProps} />);

    await userEvent.click(screen.getByRole('button', { name: /Prepare deployment/ }));
    expect(await screen.findByText('The deployment preview could not be created.')).toBeDefined();
    await userEvent.click(screen.getByRole('button', { name: /Prepare deployment/ }));
    expect(await screen.findByText(/Nothing has been sent to opsi yet/)).toBeDefined();
    expect(invokeMock).toHaveBeenCalledTimes(2);
  });

  it('keeps an uncertain confirmed rollout as a verification task', async () => {
    invokeMock.mockImplementation((_module: string, action: string) => {
      if (action === 'getRolloutPreview') return Promise.resolve(preview());
      if (action === 'requestRollout') return Promise.reject(new Error('Connection closed'));
      return Promise.reject(new Error(`Unexpected action ${action}`));
    });
    render(<PatchDeploymentWorkflow {...baseProps} />);

    await userEvent.click(screen.getByRole('button', { name: /Prepare deployment/ }));
    await userEvent.click(await screen.findByRole('checkbox', { name: /I have reviewed the affected clients/ }));
    await userEvent.click(screen.getByRole('button', { name: /Request deployment/ }));

    expect(await screen.findByText('The deployment request could not be confirmed as completed.')).toBeDefined();
    expect(screen.getByText(/First check the history and current opsi state/)).toBeDefined();
    expect(invokeMock.mock.calls.filter((call) => call[1] === 'requestRollout')).toHaveLength(1);
  });

  it('invalidates a pending preview on selection change and never requests while offline', async () => {
    const pending = deferred<RolloutPreview>();
    invokeMock.mockReturnValueOnce(pending.promise).mockResolvedValueOnce(preview('pc2.kauth.local'));
    const view = render(
      <PatchDeploymentWorkflow
        {...baseProps}
        selectedClients={new Set(['pc1.kauth.local'])}
      />,
    );
    await userEvent.click(screen.getByRole('button', { name: /Prepare deployment/ }));

    view.rerender(
      <PatchDeploymentWorkflow
        {...baseProps}
        selectedClients={new Set(['pc2.kauth.local'])}
      />,
    );
    pending.resolve(preview('pc1.kauth.local'));
    await waitFor(() => expect(screen.queryByText('pc1')).toBeNull());

    await userEvent.click(screen.getByRole('button', { name: 'Prepare deployment (1 selected)' }));
    expect(await screen.findByText('pc2')).toBeDefined();

    view.rerender(
      <PatchDeploymentWorkflow
        {...baseProps}
        connected={false}
        selectedClients={new Set(['pc2.kauth.local'])}
      >
        <button type="button">Package control</button>
      </PatchDeploymentWorkflow>,
    );
    expect(screen.getByText('Connect to opsi for previews and deployments.')).toBeDefined();
    expect(screen.getByRole('button', { name: 'Prepare deployment (1 selected)' }).hasAttribute('disabled')).toBe(true);
    expect(screen.getByRole('button', { name: 'Package control' })).toBeDefined();
    expect(invokeMock).toHaveBeenCalledTimes(2);
  });
});
