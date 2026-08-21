import { act, renderHook, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { PrinterNotificationCheck } from '../../shared/api-types';
import { usePrinterNotificationChecks } from './usePrinterNotificationChecks';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../shared/bridge/bridgeClient', () => ({
  invoke: invokeMock,
}));

function notification(host: string, status: PrinterNotificationCheck['status']): PrinterNotificationCheck {
  return { host, status, rules: [], error: null };
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

describe('usePrinterNotificationChecks', () => {
  beforeEach(() => {
    invokeMock.mockReset();
  });

  it('checks only addressed printers with exact site and default-password payloads', async () => {
    invokeMock.mockImplementation((_module: string, _action: string, payload: { host: string }) =>
      Promise.resolve(notification(payload.host, payload.host === 'printer-a' ? 'OK' : 'WARNING')));
    const { result } = renderHook(() => usePrinterNotificationChecks());

    act(() => {
      result.current.check([
        { name: 'PK-NETPRT001', deviceAddress: 'printer-a' },
        { name: 'SU-NETPRT002', deviceAddress: '10.0.0.2' },
        { name: 'No-address', deviceAddress: null },
      ]);
    });

    await waitFor(() => expect(result.current.checking).toBe(false));
    expect(invokeMock).toHaveBeenCalledTimes(2);
    expect(invokeMock).toHaveBeenNthCalledWith(
      1,
      'printmanagement',
      'checkNotificationConfig',
      { host: 'printer-a', siteCode: 'PK', password: null },
      60_000,
    );
    expect(invokeMock).toHaveBeenNthCalledWith(
      2,
      'printmanagement',
      'checkNotificationConfig',
      { host: '10.0.0.2', siteCode: 'SU', password: null },
      60_000,
    );
    expect(result.current.notifications['PRINTER-A']?.status).toBe('OK');
    expect(result.current.notifications['10.0.0.2']?.status).toBe('WARNING');
    expect(result.current.notifications['NO-ADDRESS']).toBeUndefined();
  });

  it('keeps checking visible and preserves successful results when another target fails', async () => {
    const first = deferred<PrinterNotificationCheck>();
    invokeMock.mockImplementation(
      (_module: string, _action: string, payload: { host: string }) =>
        payload.host === 'printer-a' ? first.promise : Promise.reject(new Error('unreachable')),
    );
    const { result } = renderHook(() => usePrinterNotificationChecks());

    act(() => result.current.setPassword('session-secret'));
    act(() => {
      result.current.check([
        { name: 'PK-NETPRT001', deviceAddress: 'printer-a' },
        { name: 'SU-NETPRT002', deviceAddress: 'printer-b' },
      ]);
    });

    expect(result.current.checking).toBe(true);
    expect(invokeMock).toHaveBeenCalledWith(
      'printmanagement',
      'checkNotificationConfig',
      { host: 'printer-a', siteCode: 'PK', password: 'session-secret' },
      60_000,
    );

    act(() => first.resolve(notification('printer-a', 'OK')));
    await waitFor(() => expect(result.current.checking).toBe(false));
    expect(result.current.notifications).toEqual({
      'PRINTER-A': notification('printer-a', 'OK'),
    });
  });

  it('does not enter checking state when no addressed target exists', () => {
    const { result } = renderHook(() => usePrinterNotificationChecks());

    act(() => result.current.check([{ name: 'No-address', deviceAddress: null }]));

    expect(result.current.checking).toBe(false);
    expect(result.current.notifications).toEqual({});
    expect(invokeMock).not.toHaveBeenCalled();
  });
});
