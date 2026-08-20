import { act, renderHook, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type {
  OpsiConnectionStatusResult,
  PatchDashboardOverview,
  VersionCheckResult,
} from '../../shared/api-types';
import {
  normalizeCachedPatchView,
  resolveDefaultDepot,
  usePatchManagementWorkspace,
} from './usePatchManagementWorkspace';

const { invokeMock, loadViewMock, saveViewMock } = vi.hoisted(() => ({
  invokeMock: vi.fn(),
  loadViewMock: vi.fn(),
  saveViewMock: vi.fn(),
}));

vi.mock('../../shared/bridge/bridgeClient', async (importOriginal) => {
  const original = await importOriginal<typeof import('../../shared/bridge/bridgeClient')>();
  return { ...original, invoke: invokeMock };
});

vi.mock('../../shared/viewCache', () => ({
  loadView: loadViewMock,
  saveView: saveViewMock,
}));

const connectedStatus: OpsiConnectionStatusResult = {
  connected: true,
  serverUrl: 'https://opsi.example.test:4447/',
  userName: 'admin',
  opsiVersion: '4.3.1.2',
  defaultDepotFilter: '',
  connectionError: null,
};

const disconnectedStatus: OpsiConnectionStatusResult = {
  connected: false,
  serverUrl: null,
  userName: null,
  opsiVersion: null,
  defaultDepotFilter: '',
  connectionError: null,
};

const dashboard: PatchDashboardOverview = {
  serverUrl: 'https://opsi.example.test:4447/',
  depotFilter: null,
  generatedAtUtc: '2026-08-20T08:00:00Z',
  summary: {
    productCount: 1,
    productsWithUpdates: 1,
    productsWithDepotDeviation: 0,
    productsMissingOnDepots: 0,
    productsWithFailures: 0,
    pendingRolloutCount: 0,
    outdatedClientCount: 1,
    clientCount: 2,
    depotCount: 2,
    unmappedSoftwareCount: 0,
  },
  depots: [
    { id: 'opsi.example.test', description: 'Main server', isConfigServer: true, clientCount: 0 },
    { id: 'depot-denkingen.example.test', description: 'Denkingen', isConfigServer: false, clientCount: 2 },
  ],
  products: [{
    productId: 'firefox',
    name: 'Mozilla Firefox',
    availableVersion: '128.0-2',
    referenceVersion: '128.0-2',
    manufacturerVersion: '129.0',
    manufacturerCheckStatus: 'SUCCESS',
    manufacturerCheckedAtUtc: '2026-08-20T07:55:00Z',
    manufacturerCheckError: null,
    manufacturerUpdateAvailable: true,
    depotVersions: [{ depotId: 'depot-denkingen.example.test', version: '128.0-2' }],
    missingDepotIds: [],
    packageStatus: 'UPDATE_AVAILABLE',
    state: 'UPDATE_AVAILABLE',
    installedClientCount: 2,
    outdatedClientCount: 1,
    failedClientCount: 0,
    pendingActionCount: 0,
    lastError: null,
    mappedSoftwareNames: ['Mozilla Firefox'],
    inventoryDetections: [],
  }],
  unmappedSoftware: [],
};

function dashboardCalls(): unknown[] {
  return invokeMock.mock.calls
    .filter((call) => call[1] === 'getDashboard')
    .map((call) => call[2]);
}

describe('usePatchManagementWorkspace', () => {
  beforeEach(() => {
    invokeMock.mockReset();
    loadViewMock.mockReset();
    saveViewMock.mockReset();
    loadViewMock.mockReturnValue(null);
  });

  it('restores a disconnected cached dashboard read-only and persists no credentials', async () => {
    loadViewMock.mockReturnValue({
      server: 'opsi.example.test',
      userName: 'cached-user',
      depotFilter: '',
      dashboard,
    });
    invokeMock.mockResolvedValue(disconnectedStatus);

    const { result } = renderHook(() => usePatchManagementWorkspace());

    await waitFor(() => expect(result.current.statusLoading).toBe(false));
    expect(result.current.dashboard?.products[0].productId).toBe('firefox');
    expect(result.current.connected).toBe(false);
    expect(result.current.stale).toBe(true);
    expect(dashboardCalls()).toEqual([]);
    await waitFor(() => expect(saveViewMock).toHaveBeenCalled());
    const persisted = saveViewMock.mock.calls.at(-1)?.[1];
    expect(persisted).toEqual(expect.objectContaining({
      server: 'opsi.example.test',
      userName: 'cached-user',
      dashboard: expect.any(Object),
    }));
    expect(JSON.stringify(persisted)).not.toContain('password');
  });

  it('loads a connected dashboard and resolves the configured default depot exactly once', async () => {
    invokeMock.mockImplementation((_module: string, action: string, payload?: { depotFilter?: string | null }) => {
      if (action === 'getConnectionStatus') {
        return Promise.resolve({ ...connectedStatus, defaultDepotFilter: 'Denkingen' });
      }
      if (action === 'getDashboard') {
        return Promise.resolve({ ...dashboard, depotFilter: payload?.depotFilter ?? null });
      }
      return Promise.reject(new Error(`Unexpected action ${action}`));
    });

    const { result } = renderHook(() => usePatchManagementWorkspace());

    await waitFor(() => expect(result.current.depotFilter).toBe('depot-denkingen.example.test'));
    await waitFor(() => expect(dashboardCalls()).toEqual([
      { depotFilter: null },
      { depotFilter: 'depot-denkingen.example.test' },
    ]));
    expect(result.current.connected).toBe(true);
    expect(result.current.dashboard?.depotFilter).toBe('depot-denkingen.example.test');
  });

  it('owns exact depot changes and dashboard refreshes', async () => {
    invokeMock.mockImplementation((_module: string, action: string, payload?: { depotFilter?: string | null }) => {
      if (action === 'getConnectionStatus') return Promise.resolve(connectedStatus);
      if (action === 'getDashboard') {
        return Promise.resolve({ ...dashboard, depotFilter: payload?.depotFilter ?? null });
      }
      return Promise.reject(new Error(`Unexpected action ${action}`));
    });
    const { result } = renderHook(() => usePatchManagementWorkspace());
    await waitFor(() => expect(dashboardCalls()).toEqual([{ depotFilter: null }]));

    act(() => result.current.changeDepotFilter('depot-denkingen.example.test'));
    await waitFor(() => expect(dashboardCalls()).toHaveLength(2));
    expect(dashboardCalls()[1]).toEqual({ depotFilter: 'depot-denkingen.example.test' });

    act(() => result.current.refreshDashboard());
    await waitFor(() => expect(dashboardCalls()).toHaveLength(3));
    expect(dashboardCalls()[2]).toEqual({ depotFilter: 'depot-denkingen.example.test' });
  });

  it('owns manufacturer-check busy, refresh and verification-error state', async () => {
    let rejectCheck = false;
    let resolveCheck: ((value: VersionCheckResult) => void) | undefined;
    invokeMock.mockImplementation((_module: string, action: string) => {
      if (action === 'getConnectionStatus') return Promise.resolve(connectedStatus);
      if (action === 'getDashboard') return Promise.resolve(dashboard);
      if (action === 'checkVendorVersions') {
        if (rejectCheck) return Promise.reject(new Error('Check completion is unknown'));
        return new Promise<VersionCheckResult>((resolve) => { resolveCheck = resolve; });
      }
      return Promise.reject(new Error(`Unexpected action ${action}`));
    });
    const { result } = renderHook(() => usePatchManagementWorkspace());
    await waitFor(() => expect(dashboardCalls()).toHaveLength(1));

    let successfulCheck: Promise<boolean> | undefined;
    act(() => { successfulCheck = result.current.checkVendorVersions(['firefox']); });
    expect(result.current.versionCheckBusy).toBe(true);
    expect(invokeMock).toHaveBeenCalledWith(
      'patchmanagement',
      'checkVendorVersions',
      { productIds: ['firefox'] },
    );
    await act(async () => {
      resolveCheck?.({ outcomes: [] });
      expect(await successfulCheck).toBe(true);
    });
    await waitFor(() => expect(dashboardCalls()).toHaveLength(2));
    expect(result.current.versionCheckBusy).toBe(false);

    rejectCheck = true;
    await act(async () => {
      expect(await result.current.checkVendorVersions()).toBe(false);
    });
    expect(result.current.versionCheckError?.message)
      .toBe('The manufacturer checks could not be confirmed as completed.');
    expect(result.current.versionCheckError?.action)
      .toMatch(/First check the latest check times and history/);
    act(() => result.current.clearVersionCheckError());
    expect(result.current.versionCheckError).toBeNull();
    expect(dashboardCalls()).toHaveLength(2);
  });
});

describe('patch workspace cache helpers', () => {
  it('migrates legacy product evidence and resolves depots case-insensitively', () => {
    const legacy = JSON.parse(JSON.stringify({
      server: 'opsi.example.test',
      userName: 'admin',
      depotFilter: '',
      dashboard,
    })) as { dashboard: { products: Array<Record<string, unknown>> } };
    delete legacy.dashboard.products[0].missingDepotIds;
    delete legacy.dashboard.products[0].manufacturerCheckStatus;
    delete legacy.dashboard.products[0].inventoryDetections;

    expect(normalizeCachedPatchView(legacy)?.dashboard?.products[0]).toEqual(expect.objectContaining({
      missingDepotIds: ['opsi.example.test'],
      manufacturerCheckStatus: 'NOT_CONFIGURED',
      inventoryDetections: [],
    }));
    expect(resolveDefaultDepot(dashboard.depots, 'DENKINGEN'))
      .toBe('depot-denkingen.example.test');
    expect(resolveDefaultDepot(dashboard.depots, 'unknown')).toBeNull();
  });
});
