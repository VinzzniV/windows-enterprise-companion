import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ProductVersionSource } from '../../shared/api-types';
import { PatchAutomationWorkspace } from './PatchAutomationWorkspace';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../shared/bridge/bridgeClient', async (importOriginal) => ({
  ...await importOriginal<typeof import('../../shared/bridge/bridgeClient')>(),
  invoke: invokeMock,
}));

const products = [
  { productId: 'firefox', name: 'Mozilla Firefox' },
  { productId: 'sevenzip', name: '7-Zip' },
];

function source(
  productId: string,
  overrides: Partial<ProductVersionSource> = {},
): ProductVersionSource {
  return {
    productId,
    sourceUrl: `https://vendor.example/${productId}`,
    versionPattern: '([0-9.]+)',
    enabled: true,
    latestVersion: '129.0',
    lastCheckedUtc: '2026-08-20T01:02:03Z',
    checkStatus: 'SUCCESS',
    lastError: null,
    ...overrides,
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((promiseResolve, promiseReject) => {
    resolve = promiseResolve;
    reject = promiseReject;
  });
  return { promise, resolve, reject };
}

describe('PatchAutomationWorkspace', () => {
  beforeEach(() => {
    invokeMock.mockReset();
  });

  it('loads sources on mount and presents enabled-only scheduling plus the rollout guide', async () => {
    invokeMock.mockResolvedValueOnce({
      sources: [source('firefox'), source('sevenzip', { enabled: false })],
    });

    render(
      <PatchAutomationWorkspace
        connected={false}
        products={products}
        checkBusy={false}
        checkError={null}
        onCheckAll={vi.fn().mockResolvedValue(false)}
        onClearCheckError={vi.fn()}
        onDashboardRefresh={vi.fn()}
      />,
    );

    expect(await screen.findByText('1 active')).toBeDefined();
    expect(screen.getByText('Controlled rollout')).toBeDefined();
    expect(screen.getByText('Roll out broadly')).toBeDefined();
    const disabledRow = screen.getByText('sevenzip').closest('tr') as HTMLTableRowElement;
    expect(within(disabledRow).getByText('Disabled')).toBeDefined();
    expect((screen.getByRole('button', {
      name: 'Run all checks now',
    }) as HTMLButtonElement).disabled).toBe(true);
    expect(invokeMock).toHaveBeenCalledExactlyOnceWith(
      'patchmanagement',
      'listVersionSources',
      {},
    );
  });

  it('saves the exact enabled source and resets the form after confirmation', async () => {
    invokeMock.mockImplementation((_module: string, action: string, payload?: unknown) => {
      if (action === 'listVersionSources') return Promise.resolve({ sources: [] });
      if (action === 'saveVersionSource') {
        return Promise.resolve({ sources: [source((payload as { productId: string }).productId, {
          latestVersion: null,
          lastCheckedUtc: null,
          checkStatus: 'NOT_CHECKED',
        })] });
      }
      return Promise.reject(new Error(`Unexpected action ${action}`));
    });

    render(
      <PatchAutomationWorkspace
        connected
        products={products}
        checkBusy={false}
        checkError={null}
        onCheckAll={vi.fn().mockResolvedValue(false)}
        onClearCheckError={vi.fn()}
        onDashboardRefresh={vi.fn()}
      />,
    );
    await screen.findByText('No manufacturer sources configured yet.');
    await userEvent.selectOptions(
      screen.getByRole('combobox', { name: 'opsi product for manufacturer source' }),
      'firefox',
    );
    await userEvent.type(
      screen.getByLabelText('Manufacturer source HTTPS URL'),
      'https://vendor.example/releases',
    );
    await userEvent.type(screen.getByLabelText('Version pattern'), 'Version (1.2.3)');
    await userEvent.click(screen.getByRole('button', { name: 'Save source' }));

    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith(
      'patchmanagement',
      'saveVersionSource',
      {
        productId: 'firefox',
        sourceUrl: 'https://vendor.example/releases',
        versionPattern: 'Version (1.2.3)',
        enabled: true,
      },
    ));
    expect(await screen.findByText('firefox')).toBeDefined();
    expect((screen.getByLabelText('Manufacturer source HTTPS URL') as HTMLInputElement).value)
      .toBe('');
  });

  it('keeps a failed save reviewable with state-verification guidance', async () => {
    invokeMock.mockImplementation((_module: string, action: string) => {
      if (action === 'listVersionSources') return Promise.resolve({ sources: [] });
      if (action === 'saveVersionSource') return Promise.reject(new Error('Save result lost'));
      return Promise.reject(new Error(`Unexpected action ${action}`));
    });

    render(
      <PatchAutomationWorkspace
        connected
        products={products}
        checkBusy={false}
        checkError={null}
        onCheckAll={vi.fn().mockResolvedValue(false)}
        onClearCheckError={vi.fn()}
        onDashboardRefresh={vi.fn()}
      />,
    );
    await screen.findByText('No manufacturer sources configured yet.');
    await userEvent.selectOptions(
      screen.getByRole('combobox', { name: 'opsi product for manufacturer source' }),
      'firefox',
    );
    await userEvent.type(
      screen.getByLabelText('Manufacturer source HTTPS URL'),
      'https://vendor.example/releases',
    );
    await userEvent.type(screen.getByLabelText('Version pattern'), 'Version (1.2.3)');
    await userEvent.click(screen.getByRole('button', { name: 'Save source' }));

    expect(await screen.findByText(
      'The manufacturer source could not be confirmed as saved.',
    )).toBeDefined();
    expect(screen.getByText(/First check the manufacturer sources/)).toBeDefined();
    expect((screen.getByLabelText('Manufacturer source HTTPS URL') as HTMLInputElement).value)
      .toBe('https://vendor.example/releases');
  });

  it('owns the check-all control state and refreshes sources after a confirmed callback', async () => {
    const check = deferred<boolean>();
    invokeMock.mockImplementation((_module: string, action: string) => {
      if (action === 'listVersionSources') return Promise.resolve({ sources: [] });
      return Promise.reject(new Error(`Unexpected action ${action}`));
    });
    const checkAll = vi.fn(() => check.promise);
    const refreshDashboard = vi.fn();

    const { rerender } = render(
      <PatchAutomationWorkspace
        connected
        products={products}
        checkBusy={false}
        checkError={null}
        onCheckAll={checkAll}
        onClearCheckError={vi.fn()}
        onDashboardRefresh={refreshDashboard}
      />,
    );
    await screen.findByText('No manufacturer sources configured yet.');
    await userEvent.click(screen.getByRole('button', { name: 'Run all checks now' }));
    expect(checkAll).toHaveBeenCalledTimes(1);
    rerender(
      <PatchAutomationWorkspace
        connected
        products={products}
        checkBusy
        checkError={null}
        onCheckAll={checkAll}
        onClearCheckError={vi.fn()}
        onDashboardRefresh={refreshDashboard}
      />,
    );
    expect((screen.getByRole('button', {
      name: 'Running checks…',
    }) as HTMLButtonElement).disabled).toBe(true);

    act(() => check.resolve(true));
    expect(invokeMock.mock.calls.filter((call) => call[1] === 'listVersionSources'))
      .toHaveLength(1);
    await waitFor(() => expect(
      invokeMock.mock.calls.filter((call) => call[1] === 'listVersionSources'),
    ).toHaveLength(2));
  });

  it('removes a source exactly and refreshes the connected dashboard only after confirmation', async () => {
    invokeMock.mockImplementation((_module: string, action: string, payload?: unknown) => {
      if (action === 'listVersionSources') return Promise.resolve({ sources: [source('firefox')] });
      if (action === 'deleteVersionSource') {
        expect(payload).toEqual({ productId: 'firefox' });
        return Promise.resolve({ sources: [] });
      }
      return Promise.reject(new Error(`Unexpected action ${action}`));
    });
    const refreshDashboard = vi.fn();

    render(
      <PatchAutomationWorkspace
        connected
        products={products}
        checkBusy={false}
        checkError={null}
        onCheckAll={vi.fn().mockResolvedValue(false)}
        onClearCheckError={vi.fn()}
        onDashboardRefresh={refreshDashboard}
      />,
    );
    await userEvent.click(await screen.findByRole('button', { name: 'Remove' }));

    await screen.findByText('No manufacturer sources configured yet.');
    expect(refreshDashboard).toHaveBeenCalledTimes(1);
    expect(invokeMock.mock.calls.filter((call) => call[1] === 'deleteVersionSource'))
      .toHaveLength(1);
  });
});
