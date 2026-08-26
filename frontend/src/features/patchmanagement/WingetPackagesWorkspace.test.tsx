import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { WingetManagedPackageView, WingetPackagePreview } from '../../shared/api-types';
import { WingetPackagesWorkspace } from './WingetPackagesWorkspace';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../shared/bridge/bridgeClient', async (importOriginal) => {
  const original = await importOriginal<typeof import('../../shared/bridge/bridgeClient')>();
  return { ...original, invoke: invokeMock };
});

const depot = { id: 'depot-test.example.test', description: 'Test', isConfigServer: true, clientCount: 2 };
const managed: WingetManagedPackageView = {
  opsiProductId: '7zip', displayName: '7-Zip', wingetId: '7zip.7zip', source: 'winget',
  scope: 'machine', depotId: depot.id, currentDepotVersion: '25.01-1',
  lastPackagedWingetVersion: '25.01', latestWingetVersion: '26.02', updateAvailable: true,
  checkStatus: 'SUCCESS', checkedAtUtc: '2026-08-26T08:00:00Z', lastError: null,
};
const preview: WingetPackagePreview = {
  opsiProductId: '7zip', displayName: '7-Zip', wingetId: '7zip.7zip', wingetVersion: '26.02',
  source: 'winget', scope: 'machine', installerType: 'Wix', architecture: 'X64', depotId: depot.id,
  currentDepotVersion: '25.01-1', targetDepotVersion: '26.02-1', packageVersion: 1,
  adoptsExistingProduct: true, workbenchPath: '/var/lib/opsi/workbench/packages/wec-winget/7zip',
  commands: ['winget install --id 7zip.7zip --version 26.02'],
  confirmationText: "Replace existing opsi product '7zip'.", generatedAtUtc: '2026-08-26T08:00:00Z',
};

describe('WingetPackagesWorkspace', () => {
  const scrollIntoView = vi.fn();

  beforeEach(() => {
    scrollIntoView.mockClear();
    Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', {
      configurable: true,
      value: scrollIntoView,
    });
    invokeMock.mockReset();
    invokeMock.mockImplementation((_module: string, action: string) => {
      if (action === 'checkWingetUpdates') return Promise.resolve({ checkedCount: 0, updateCount: 0, failedCount: 0, packages: [] });
      if (action === 'searchWingetPackages') return Promise.resolve({ packages: [{ id: '7zip.7zip', name: '7-Zip', publisher: 'Igor Pavlov', version: '26.02', source: 'winget', installerType: 'Wix', architecture: 'X64', scope: 'machine', isEligible: true, ineligibilityReason: null }] });
      if (action === 'previewWingetPackage') return Promise.resolve(preview);
      if (action === 'createOrAdoptWingetPackage') return Promise.resolve({ opsiProductId: '7zip', depotId: depot.id, success: true, oldVersion: '25.01-1', newVersion: '26.02-1', error: null });
      if (action === 'listManagedWingetPackages') return Promise.resolve({ packages: [managed] });
      throw new Error(`Unexpected action ${action}`);
    });
  });

  it('scrolls to the package form after choosing a search result', async () => {
    render(<WingetPackagesWorkspace connected depots={[depot]} preferredDepot={depot.id} onChanged={vi.fn()} />);
    await userEvent.type(screen.getByPlaceholderText('Name or exact Winget ID'), '7zip');
    await userEvent.click(screen.getByRole('button', { name: 'Search' }));
    await userEvent.click(await screen.findByRole('button', { name: 'Use package' }));

    await waitFor(() => expect(scrollIntoView).toHaveBeenCalledWith({ behavior: 'smooth', block: 'start' }));
    expect(screen.getByText('Create or adopt an opsi package')).toBeDefined();
  });

  it('requires preview and explicit confirmation before adopting an existing Product ID', async () => {
    render(<WingetPackagesWorkspace connected depots={[depot]} preferredDepot={depot.id} onChanged={vi.fn()} />);
    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith('patchmanagement', 'checkWingetUpdates', { productIds: null, force: false }));

    await userEvent.type(screen.getByPlaceholderText('Name or exact Winget ID'), '7zip');
    await userEvent.click(screen.getByRole('button', { name: 'Search' }));
    await userEvent.click(await screen.findByRole('button', { name: 'Use package' }));
    const productId = screen.getByLabelText('opsi Product ID');
    await userEvent.clear(productId);
    await userEvent.type(productId, '7zip');
    await userEvent.click(screen.getByRole('button', { name: 'Preview package' }));

    expect(await screen.findByText("Replace existing opsi product '7zip'.")).toBeDefined();
    expect(invokeMock.mock.calls.some((call) => call[1] === 'createOrAdoptWingetPackage')).toBe(false);
    await userEvent.click(screen.getByRole('button', { name: 'Confirm adoption and build' }));
    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith(
      'patchmanagement', 'createOrAdoptWingetPackage', expect.objectContaining({
        opsiProductId: '7zip', expectedWingetVersion: '26.02', confirmed: true,
      }),
    ));
  });

  it('previews a selected batch and never requests a client action', async () => {
    invokeMock.mockImplementation((_module: string, action: string) => {
      if (action === 'checkWingetUpdates') return Promise.resolve({ checkedCount: 1, updateCount: 1, failedCount: 0, packages: [managed] });
      if (action === 'prepareWingetUpdates') return Promise.resolve({ packages: [preview], confirmationText: 'Build one depot package.', generatedAtUtc: preview.generatedAtUtc });
      if (action === 'applyWingetUpdates') return Promise.resolve({ succeededCount: 1, failedCount: 0, packages: [{ opsiProductId: '7zip', depotId: depot.id, success: true, oldVersion: '25.01-1', newVersion: '26.02-1', error: null }] });
      if (action === 'listManagedWingetPackages') return Promise.resolve({ packages: [{ ...managed, currentDepotVersion: '26.02-1', updateAvailable: false }] });
      throw new Error(`Unexpected action ${action}`);
    });

    render(<WingetPackagesWorkspace connected depots={[depot]} preferredDepot={depot.id} onChanged={vi.fn()} />);
    await userEvent.click(await screen.findByLabelText('Select 7zip'));
    await userEvent.click(screen.getByRole('button', { name: 'Preview selected updates' }));
    expect(await screen.findByText('This only updates packages on the depot. WEC will not request any client action.')).toBeDefined();
    await userEvent.click(screen.getByRole('button', { name: 'Confirm and build selected updates' }));

    await waitFor(() => expect(invokeMock.mock.calls.some((call) => call[1] === 'applyWingetUpdates')).toBe(true));
    expect(invokeMock.mock.calls.some((call) => ['requestRollout', 'requestSetup'].includes(call[1]))).toBe(false);
  });

  it('shows unsuitable catalog results but does not allow packaging them', async () => {
    invokeMock.mockImplementation((_module: string, action: string) => {
      if (action === 'checkWingetUpdates') return Promise.resolve({ checkedCount: 0, updateCount: 0, failedCount: 0, packages: [] });
      if (action === 'searchWingetPackages') return Promise.resolve({ packages: [
        { id: '7zip.7zip', name: '7-Zip', publisher: 'Igor Pavlov', version: '26.02', source: 'winget', installerType: 'Wix', architecture: 'X64', scope: 'machine', isEligible: true, ineligibilityReason: null },
        { id: 'Example.Portable', name: 'Portable example', publisher: 'Example', version: '1.0', source: 'winget', installerType: 'Portable', architecture: 'X64', scope: 'user', isEligible: false, ineligibilityReason: 'Portable packages are not supported.' },
      ] });
      throw new Error(`Unexpected action ${action}`);
    });

    render(<WingetPackagesWorkspace connected depots={[depot]} preferredDepot={depot.id} onChanged={vi.fn()} />);
    await userEvent.type(screen.getByPlaceholderText('Name or exact Winget ID'), 'zip');
    await userEvent.click(screen.getByRole('button', { name: 'Search' }));

    expect(await screen.findByText('Not eligible')).toBeDefined();
    const useButtons = screen.getAllByRole('button', { name: 'Use package' });
    expect(useButtons).toHaveLength(2);
    expect(useButtons[0].hasAttribute('disabled')).toBe(false);
    expect(useButtons[1].hasAttribute('disabled')).toBe(true);
  });

  it('keeps managed package data visible when the daily Winget check fails', async () => {
    invokeMock.mockImplementation((_module: string, action: string) => {
      if (action === 'checkWingetUpdates') return Promise.reject(new Error('catalog offline'));
      if (action === 'listManagedWingetPackages') return Promise.resolve({ packages: [managed] });
      throw new Error(`Unexpected action ${action}`);
    });

    render(<WingetPackagesWorkspace connected depots={[depot]} preferredDepot={depot.id} onChanged={vi.fn()} />);

    expect(await screen.findByText('Winget versions could not be checked. The opsi overview remains available.')).toBeDefined();
    expect(await screen.findByText('7zip.7zip')).toBeDefined();
  });

  it('shows check feedback and package details while keeping current packages unselectable', async () => {
    const current = { ...managed, latestWingetVersion: '25.01', updateAvailable: false };
    invokeMock.mockImplementation((_module: string, action: string) => {
      if (action === 'checkWingetUpdates') return Promise.resolve({ checkedCount: 1, updateCount: 0, failedCount: 0, packages: [current] });
      throw new Error(`Unexpected action ${action}`);
    });

    render(<WingetPackagesWorkspace connected depots={[depot]} preferredDepot={depot.id} onChanged={vi.fn()} />);
    expect(await screen.findByText('Check complete: 1 checked, 0 updates available, 0 failed.')).toBeDefined();
    expect(screen.getByLabelText('Select 7zip').hasAttribute('disabled')).toBe(true);

    await userEvent.click(screen.getByText('7-Zip'));
    expect(await screen.findByText('This package is current. The update checkbox becomes available automatically when Check now finds a newer Winget version.')).toBeDefined();
    expect(screen.getByText(/generates a new opsi package whose product version matches Winget/)).toBeDefined();
  });
});
