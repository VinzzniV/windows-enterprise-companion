import { act, renderHook, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type {
  ListPrintServersResult,
  PrintServerSnapshot,
  SavedTarget,
  TargetRequest,
} from '../../shared/api-types';
import { usePrintServerWorkspace } from './usePrintServerWorkspace';

const { invokeMock, presentErrorMock } = vi.hoisted(() => ({
  invokeMock: vi.fn(),
  presentErrorMock: vi.fn((error: unknown) => ({
    message: 'The print server could not be scanned.',
    cause: 'Scan failed.',
    action: 'Retry the scan.',
    technicalDetails: String(error),
  })),
}));

vi.mock('../../shared/bridge/bridgeClient', () => ({ invoke: invokeMock }));
vi.mock('../../shared/bridge/errorPresentation', () => ({ presentError: presentErrorMock }));

function snapshot(server: string): PrintServerSnapshot {
  return {
    server,
    capturedAtUtc: '2026-08-20T01:02:03Z',
    printers: [],
    unusedPorts: [],
    unusedDrivers: [],
  };
}

function savedTarget(id: number, host: string): SavedTarget {
  return {
    id,
    label: host,
    host,
    role: 'PrintServer',
    userName: null,
    createdAtUtc: '2026-08-20T00:00:00Z',
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

const toServerRequest = vi.fn((host: string): TargetRequest => ({
  host,
  userName: 'operator',
  domain: 'EXAMPLE',
  password: 'session-password',
}));

describe('usePrintServerWorkspace', () => {
  beforeEach(() => {
    invokeMock.mockReset();
    presentErrorMock.mockClear();
    toServerRequest.mockClear();
  });

  it('restores stored snapshots without scanning and merges saved targets case-insensitively', async () => {
    invokeMock.mockImplementation((_module: string, action: string, payload?: unknown) => {
      if (action === 'getAppInfo') return Promise.resolve({ maxParallelScans: 2 });
      if (action === 'listServers') {
        return Promise.resolve({
          servers: [
            { server: 'PR-SNAPSHOT', capturedAtUtc: '2026-08-20T01:02:03Z', snapshotCount: 1 },
            { server: 'PR-MISSING', capturedAtUtc: null, snapshotCount: 0 },
          ],
        });
      }
      if (action === 'getLatest') {
        const server = (payload as { server: string }).server;
        return server === 'PR-SNAPSHOT'
          ? Promise.resolve(snapshot(server))
          : Promise.reject(new Error('missing snapshot'));
      }
      return Promise.reject(new Error(`Unexpected action ${action}`));
    });
    const refreshHints = vi.fn();

    const { result } = renderHook(() => usePrintServerWorkspace({
      savedTargets: [savedTarget(1, 'pr-snapshot'), savedTarget(2, 'PR-SAVED')],
      toServerRequest,
      onRefreshHints: refreshHints,
    }));

    await waitFor(() => expect(refreshHints).toHaveBeenCalledTimes(1));
    expect(result.current.servers).toEqual(['PR-SNAPSHOT']);
    expect(new Set(result.current.managedServers)).toEqual(new Set(['pr-snapshot', 'PR-SAVED']));
    expect(result.current.snapshots['PR-SNAPSHOT']).toEqual(snapshot('PR-SNAPSHOT'));
    expect(result.current.scanStates['PR-SNAPSHOT']).toEqual({ status: 'done' });
    expect(result.current.scanStates['PR-MISSING']).toBeUndefined();
    expect(invokeMock.mock.calls.some((call) => call[1] === 'scanServer')).toBe(false);
  });

  it('exposes the stored-snapshot restore lifecycle before first-run can be decided', async () => {
    const storedServers = deferred<ListPrintServersResult>();
    invokeMock.mockImplementation((_module: string, action: string) => {
      if (action === 'getAppInfo') return Promise.resolve({ maxParallelScans: 4 });
      if (action === 'listServers') return storedServers.promise;
      return Promise.reject(new Error(`Unexpected action ${action}`));
    });

    const { result } = renderHook(() => usePrintServerWorkspace({
      savedTargets: [],
      toServerRequest,
      onRefreshHints: vi.fn(),
    }));

    expect(result.current.restoring).toBe(true);
    await act(async () => {
      await Promise.resolve();
      await Promise.resolve();
    });
    expect(result.current.restoring).toBe(true);
    act(() => storedServers.resolve({ servers: [] }));
    await waitFor(() => expect(result.current.restoring).toBe(false));
  });

  it('owns loading, exact scan requests, success, failure and the settled hint refresh', async () => {
    const scanA = deferred<PrintServerSnapshot>();
    const scanB = deferred<PrintServerSnapshot>();
    invokeMock.mockImplementation((_module: string, action: string, payload?: unknown) => {
      if (action === 'getAppInfo') return Promise.resolve({ maxParallelScans: 2 });
      if (action === 'listServers') return Promise.resolve({ servers: [] });
      if (action === 'scanServer') {
        const host = ((payload as { target: TargetRequest }).target.host ?? '').toUpperCase();
        return host === 'PRINT-A' ? scanA.promise : scanB.promise;
      }
      return Promise.reject(new Error(`Unexpected action ${action}`));
    });
    const refreshHints = vi.fn();
    const { result } = renderHook(() => usePrintServerWorkspace({
      savedTargets: [],
      toServerRequest,
      onRefreshHints: refreshHints,
    }));
    await waitFor(() => expect(refreshHints).toHaveBeenCalledTimes(1));
    refreshHints.mockClear();

    act(() => result.current.scanServers(['print-a', 'print-b']));
    expect(result.current.scanning).toBe(true);
    expect(result.current.scanStates['PRINT-A']).toEqual({ status: 'loading' });
    expect(result.current.scanStates['PRINT-B']).toEqual({ status: 'loading' });
    expect(invokeMock).toHaveBeenCalledWith(
      'printmanagement',
      'scanServer',
      {
        target: {
          host: 'print-a',
          userName: 'operator',
          domain: 'EXAMPLE',
          password: 'session-password',
        },
      },
      300_000,
    );
    expect(invokeMock).toHaveBeenCalledWith(
      'printmanagement',
      'scanServer',
      {
        target: {
          host: 'print-b',
          userName: 'operator',
          domain: 'EXAMPLE',
          password: 'session-password',
        },
      },
      300_000,
    );

    act(() => scanA.resolve(snapshot('PRINT-A')));
    act(() => scanB.reject(new Error('print-b unavailable')));
    await waitFor(() => expect(result.current.scanning).toBe(false));
    expect(result.current.snapshots['PRINT-A']).toEqual(snapshot('PRINT-A'));
    expect(result.current.scanStates['PRINT-A']).toEqual({ status: 'done' });
    expect(result.current.scanStates['PRINT-B']?.status).toBe('error');
    expect(result.current.scanStates['PRINT-B']?.error?.message).toBe(
      'The print server could not be scanned.',
    );
    expect(presentErrorMock).toHaveBeenCalledWith(
      expect.any(Error),
      { message: 'The print server could not be scanned.' },
    );
    expect(refreshHints).toHaveBeenCalledTimes(1);
  });

  it('trims and persists additions, then removes host, target and local state', async () => {
    invokeMock.mockImplementation((_module: string, action: string, payload?: unknown) => {
      if (action === 'getAppInfo') return Promise.resolve({ maxParallelScans: 4 });
      if (action === 'listServers') {
        return Promise.resolve({
          servers: [{ server: 'PR-OLD', capturedAtUtc: '2026-08-20T01:02:03Z', snapshotCount: 1 }],
        });
      }
      if (action === 'getLatest') return Promise.resolve(snapshot('PR-OLD'));
      if (action === 'scanServer') {
        const host = (payload as { target: TargetRequest }).target.host ?? '';
        return Promise.resolve(snapshot(host.toUpperCase()));
      }
      if (action === 'deleteServer') return Promise.resolve({});
      return Promise.reject(new Error(`Unexpected action ${action}`));
    });
    const refreshHints = vi.fn();
    const saveTarget = vi.fn().mockResolvedValue(undefined);
    const deleteTarget = vi.fn().mockResolvedValue(undefined);
    const { result } = renderHook(() => usePrintServerWorkspace({
      savedTargets: [savedTarget(7, 'PR-OLD')],
      toServerRequest,
      onRefreshHints: refreshHints,
      saveTarget,
      deleteTarget,
    }));
    await waitFor(() => expect(result.current.snapshots['PR-OLD']).toBeDefined());

    act(() => result.current.setNewServer('  print-new  '));
    act(() => result.current.addServer());
    expect(result.current.newServer).toBe('');
    expect(saveTarget).toHaveBeenCalledExactlyOnceWith({
      label: 'print-new',
      host: 'print-new',
      role: 'PrintServer',
    });
    await waitFor(() => expect(result.current.snapshots['PRINT-NEW']).toBeDefined());

    act(() => result.current.removeServer('PR-OLD'));
    expect(invokeMock).toHaveBeenCalledWith('printmanagement', 'deleteServer', { server: 'PR-OLD' });
    expect(deleteTarget).toHaveBeenCalledExactlyOnceWith(7);
    expect(result.current.snapshots['PR-OLD']).toBeUndefined();
    expect(result.current.scanStates['PR-OLD']).toBeUndefined();
  });
});
