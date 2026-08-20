import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type {
  PackageUpdateOutcome,
  PackageWorkflowStatus,
  PatchDepotSummary,
  PreparePackagesPlan,
} from '../../shared/api-types';
import { PatchPackageApprovalWorkflow } from './PatchPackageApprovalWorkflow';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../shared/bridge/bridgeClient', async (importOriginal) => {
  const original = await importOriginal<typeof import('../../shared/bridge/bridgeClient')>();
  return { ...original, invoke: invokeMock };
});

const depots: PatchDepotSummary[] = [
  { id: 'depot-main', description: 'Main depot', isConfigServer: true, clientCount: 10 },
  { id: 'depot-test', description: 'Test depot', isConfigServer: false, clientCount: 2 },
];

function workflow(overrides: Partial<PackageWorkflowStatus> = {}): PackageWorkflowStatus {
  return {
    productId: 'firefox',
    testDepotId: null,
    testedVersion: null,
    testUpdateSucceededAtUtc: null,
    pilotApprovedAtUtc: null,
    pilotApproved: false,
    lastSynchronizationResult: null,
    lastSynchronizationAtUtc: null,
    lastError: null,
    ...overrides,
  };
}

function plan(stage: 'TEST' | 'DEPOT_SYNC' = 'TEST'): PreparePackagesPlan {
  const depotId = stage === 'TEST' ? 'depot-test' : 'depot-main';
  return {
    productId: 'firefox',
    stage,
    mode: 'REPOSITORY',
    artifactVersion: null,
    targets: [{
      depotId,
      host: depotId,
      currentVersion: '128.0-2',
      command: 'opsi-package-updater -v update firefox',
    }],
    note: stage === 'TEST' ? 'Test depot only.' : 'Synchronize remaining depots.',
    confirmationText: "I want to update 'firefox' on the selected depot.",
    generatedAtUtc: '2026-08-20T08:01:00Z',
  };
}

const updateOutcome: PackageUpdateOutcome = {
  productId: 'firefox',
  stage: 'TEST',
  succeededTargetCount: 1,
  failedTargetCount: 0,
  targets: [{
    depotId: 'depot-test',
    success: true,
    exitCode: 0,
    oldVersion: '128.0-2',
    newVersion: '129.0-1',
    error: null,
  }],
};

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((resolvePromise) => { resolve = resolvePromise; });
  return { promise, resolve };
}

const baseProps = {
  connected: true,
  productId: 'firefox',
  depots,
  onDashboardRefresh: vi.fn(),
};

describe('PatchPackageApprovalWorkflow', () => {
  beforeEach(() => {
    invokeMock.mockReset();
    baseProps.onDashboardRefresh.mockReset();
  });

  it('loads the exact approval status, shows loading honestly and selects the preferred test depot', async () => {
    const pending = deferred<PackageWorkflowStatus>();
    invokeMock.mockReturnValue(pending.promise);
    render(
      <PatchPackageApprovalWorkflow {...baseProps}>
        <p>Deployment child</p>
      </PatchPackageApprovalWorkflow>,
    );

    expect(screen.getByText('Running')).toBeDefined();
    expect(screen.getByText('Loading the current approval status…')).toBeDefined();
    expect(screen.getByText('Deployment child')).toBeDefined();
    expect((screen.getByRole('combobox', { name: 'Test depot' }) as HTMLSelectElement).value)
      .toBe('depot-test');
    expect(invokeMock).toHaveBeenCalledWith(
      'patchmanagement',
      'getPackageWorkflowStatus',
      { productId: 'firefox' },
    );

    pending.resolve(workflow());
    expect(await screen.findByText('Update one depot first, deploy to test clients, and verify the result.')).toBeDefined();
  });

  it('keeps an unavailable approval status local and retries only its read request', async () => {
    invokeMock.mockRejectedValueOnce(new Error('Status unavailable')).mockResolvedValueOnce(workflow());
    render(<PatchPackageApprovalWorkflow {...baseProps} />);

    expect(await screen.findByText('The package approval status could not be loaded.')).toBeDefined();
    await userEvent.click(screen.getByRole('button', { name: 'Reload approval status' }));
    expect(await screen.findByText('Update one depot first, deploy to test clients, and verify the result.')).toBeDefined();
    expect(invokeMock).toHaveBeenCalledTimes(2);
  });

  it('discards a late status response after the selected product changes', async () => {
    const firefox = deferred<PackageWorkflowStatus>();
    invokeMock
      .mockReturnValueOnce(firefox.promise)
      .mockResolvedValueOnce(workflow({ productId: 'sevenzip' }));
    const view = render(<PatchPackageApprovalWorkflow {...baseProps} />);

    view.rerender(
      <PatchPackageApprovalWorkflow
        {...baseProps}
        productId="sevenzip"
      />,
    );
    expect(await screen.findByText('Update one depot first, deploy to test clients, and verify the result.')).toBeDefined();
    firefox.resolve(workflow({
      testDepotId: 'depot-test',
      testedVersion: '129.0-1',
      testUpdateSucceededAtUtc: '2026-08-20T08:02:00Z',
      pilotApprovedAtUtc: '2026-08-20T08:03:00Z',
      pilotApproved: true,
    }));
    await waitFor(() => expect(screen.queryByText('Pilot approved')).toBeNull());
  });

  it('discards a late package plan after the selected product changes', async () => {
    const firefoxPlan = deferred<PreparePackagesPlan>();
    invokeMock.mockImplementation((_module: string, action: string, payload: unknown) => {
      if (action === 'getPackageWorkflowStatus') {
        const requested = (payload as { productId: string }).productId;
        return Promise.resolve(workflow({ productId: requested }));
      }
      if (action === 'preparePackages') return firefoxPlan.promise;
      return Promise.reject(new Error(`Unexpected action ${action}`));
    });
    const view = render(<PatchPackageApprovalWorkflow {...baseProps} />);

    await userEvent.click(await screen.findByRole('button', { name: 'Prepare test update' }));
    view.rerender(<PatchPackageApprovalWorkflow {...baseProps} productId="sevenzip" />);
    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith(
      'patchmanagement',
      'getPackageWorkflowStatus',
      { productId: 'sevenzip' },
    ));
    await act(async () => firefoxPlan.resolve(plan()));

    expect(screen.queryByText('opsi-package-updater -v update firefox')).toBeNull();
    expect(screen.getByText('Update one depot first, deploy to test clients, and verify the result.')).toBeDefined();
  });

  it('previews and explicitly confirms the exact test-depot package action', async () => {
    invokeMock.mockImplementation((_module: string, action: string) => {
      if (action === 'getPackageWorkflowStatus') return Promise.resolve(workflow());
      if (action === 'preparePackages') return Promise.resolve(plan());
      if (action === 'executePackageUpdate') return Promise.resolve(updateOutcome);
      return Promise.reject(new Error(`Unexpected action ${action}`));
    });
    const onDashboardRefresh = vi.fn();
    render(
      <PatchPackageApprovalWorkflow
        {...baseProps}
        onDashboardRefresh={onDashboardRefresh}
      />,
    );

    await userEvent.click(await screen.findByRole('button', { name: 'Prepare test update' }));
    expect(invokeMock).toHaveBeenCalledWith('patchmanagement', 'preparePackages', {
      productId: 'firefox',
      stage: 'TEST',
      depotIds: ['depot-test'],
    });
    expect(await screen.findByText('opsi-package-updater -v update firefox')).toBeDefined();
    const execute = screen.getByRole('button', { name: 'Confirm and run over SSH' });
    expect(execute.hasAttribute('disabled')).toBe(true);

    await userEvent.click(screen.getByRole('checkbox', { name: /I want to update 'firefox'/ }));
    await userEvent.click(execute);
    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith(
      'patchmanagement',
      'executePackageUpdate',
      {
        productId: 'firefox',
        stage: 'TEST',
        depotIds: ['depot-test'],
        confirmed: true,
      },
    ));
    expect(await screen.findByText(/Package action completed: 1 succeeded/)).toBeDefined();
    expect(onDashboardRefresh).toHaveBeenCalledTimes(1);
    expect(invokeMock.mock.calls.filter((call) => call[1] === 'getPackageWorkflowStatus')).toHaveLength(2);
  });

  it('keeps a failed confirmed package action reviewable without retrying it', async () => {
    invokeMock.mockImplementation((_module: string, action: string) => {
      if (action === 'getPackageWorkflowStatus') return Promise.resolve(workflow());
      if (action === 'preparePackages') return Promise.resolve(plan());
      if (action === 'executePackageUpdate') return Promise.reject(new Error('SSH result lost'));
      return Promise.reject(new Error(`Unexpected action ${action}`));
    });
    render(<PatchPackageApprovalWorkflow {...baseProps} />);

    await userEvent.click(await screen.findByRole('button', { name: 'Prepare test update' }));
    await userEvent.click(await screen.findByRole('checkbox', { name: /I want to update 'firefox'/ }));
    await userEvent.click(screen.getByRole('button', { name: 'Confirm and run over SSH' }));

    expect(await screen.findByText('The package action could not be confirmed as completed.')).toBeDefined();
    expect(screen.getByText(/First check the history and package status on the target depots/)).toBeDefined();
    expect(screen.getByText('opsi-package-updater -v update firefox')).toBeDefined();
    expect(invokeMock.mock.calls.filter((call) => call[1] === 'executePackageUpdate')).toHaveLength(1);
  });

  it('requires an explicit pilot approval and then unlocks depot synchronization without the test depot', async () => {
    const tested = workflow({
      testDepotId: 'depot-test',
      testedVersion: '129.0-1',
      testUpdateSucceededAtUtc: '2026-08-20T08:02:00Z',
    });
    invokeMock.mockImplementation((_module: string, action: string) => {
      if (action === 'getPackageWorkflowStatus') return Promise.resolve(tested);
      if (action === 'approvePackagePilot') {
        return Promise.resolve({
          ...tested,
          pilotApproved: true,
          pilotApprovedAtUtc: '2026-08-20T08:03:00Z',
        });
      }
      if (action === 'preparePackages') return Promise.resolve(plan('DEPOT_SYNC'));
      return Promise.reject(new Error(`Unexpected action ${action}`));
    });
    render(<PatchPackageApprovalWorkflow {...baseProps} />);

    const approve = await screen.findByRole('button', { name: 'Approve pilot' });
    expect(approve.hasAttribute('disabled')).toBe(true);
    await userEvent.click(screen.getByRole('checkbox', { name: /I approve this package version/ }));
    await userEvent.click(approve);
    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith(
      'patchmanagement',
      'approvePackagePilot',
      { productId: 'firefox', confirmed: true },
    ));

    const sync = screen.getByRole('button', { name: 'Distribute to additional depots' });
    await waitFor(() => expect(sync.hasAttribute('disabled')).toBe(false));
    await userEvent.click(sync);
    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith(
      'patchmanagement',
      'preparePackages',
      { productId: 'firefox', stage: 'DEPOT_SYNC', depotIds: ['depot-main'] },
    ));
  });

  it('never requests package state while offline and preserves the deployment child', () => {
    render(
      <PatchPackageApprovalWorkflow {...baseProps} connected={false}>
        <p>Offline deployment child</p>
      </PatchPackageApprovalWorkflow>,
    );

    expect(screen.getByText('Connect to opsi to verify the current approval status.')).toBeDefined();
    expect(screen.getByText('Offline deployment child')).toBeDefined();
    expect(screen.getByRole('button', { name: 'Prepare test update' }).hasAttribute('disabled')).toBe(true);
    expect(screen.getByRole('button', { name: 'Distribute to additional depots' }).hasAttribute('disabled')).toBe(true);
    expect(invokeMock).not.toHaveBeenCalled();
  });
});
