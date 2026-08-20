import { act, renderHook, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { DhcpCheckResult, TargetRequest } from '../../shared/api-types';
import { usePrinterDhcpCheck } from './usePrinterDhcpCheck';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../shared/bridge/bridgeClient', () => ({
  invoke: invokeMock,
  BridgeInvokeError: class extends Error {},
}));

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((promiseResolve) => {
    resolve = promiseResolve;
  });
  return { promise, resolve };
}

const toServerRequest = vi.fn((host: string): TargetRequest => ({
  host,
  userName: 'operator',
  domain: 'EXAMPLE',
  password: 'session-password',
}));

describe('usePrinterDhcpCheck', () => {
  beforeEach(() => {
    invokeMock.mockReset();
    toServerRequest.mockClear();
  });

  it('applies a late configured default only while the server input is empty', async () => {
    const { result, rerender } = renderHook(
      ({ configuredServer }) => usePrinterDhcpCheck({ configuredServer, toServerRequest }),
      { initialProps: { configuredServer: null as string | null } },
    );

    expect(result.current.server).toBe('');
    rerender({ configuredServer: 'dc01' });
    await waitFor(() => expect(result.current.server).toBe('dc01'));

    act(() => result.current.setServer('custom-dhcp'));
    rerender({ configuredServer: 'dc02' });
    expect(result.current.server).toBe('custom-dhcp');
  });

  it('returns local validation errors without invoking the bridge', () => {
    const { result } = renderHook(() => usePrinterDhcpCheck({
      configuredServer: null,
      toServerRequest,
    }));

    act(() => result.current.check([{ deviceIp: '10.0.0.10' }]));
    expect(result.current.error).toBe('Enter a DHCP server.');

    act(() => result.current.setServer('dc01'));
    act(() => result.current.check([{ deviceIp: null }]));
    expect(result.current.error).toBe('No resolved IP addresses are available. Rescan first.');
    expect(invokeMock).not.toHaveBeenCalled();
  });

  it('owns the exact request, atomic coverage result, invalidation and technical error', async () => {
    const request = deferred<DhcpCheckResult>();
    invokeMock.mockReturnValueOnce(request.promise);
    const { result } = renderHook(() => usePrinterDhcpCheck({
      configuredServer: 'dc01',
      toServerRequest,
    }));
    await waitFor(() => expect(result.current.server).toBe('dc01'));

    act(() => result.current.check([
      { deviceIp: '10.0.0.10' },
      { deviceIp: '10.0.0.10' },
      { deviceIp: '10.0.0.11' },
      { deviceIp: null },
    ]));

    expect(result.current.checking).toBe(true);
    expect(result.current.result).toBeNull();
    expect(toServerRequest).toHaveBeenCalledExactlyOnceWith('dc01');
    expect(invokeMock).toHaveBeenCalledExactlyOnceWith(
      'printmanagement',
      'checkDhcp',
      {
        target: {
          host: 'dc01',
          userName: 'operator',
          domain: 'EXAMPLE',
          password: 'session-password',
        },
        ips: ['10.0.0.10', '10.0.0.11'],
      },
      60_000,
    );

    act(() => request.resolve({
      reserved: [{ ip: '10.0.0.10', mac: '00-11-22-33-44-55', name: 'PRINTER-10' }],
    }));
    await waitFor(() => expect(result.current.checking).toBe(false));
    expect([...result.current.checkedIps]).toEqual(['10.0.0.10', '10.0.0.11']);
    expect(result.current.reservations).toEqual({
      '10.0.0.10': { ip: '10.0.0.10', mac: '00-11-22-33-44-55', name: 'PRINTER-10' },
    });
    expect(result.current.result?.server).toBe('dc01');

    act(() => result.current.setServer('dc02'));
    expect(result.current.result).toBeNull();
    expect(result.current.checkedIps.size).toBe(0);
    expect(result.current.reservations).toEqual({});

    invokeMock.mockRejectedValueOnce(new Error('DHCP endpoint unavailable'));
    act(() => result.current.check([{ deviceIp: '10.0.0.12' }]));
    await waitFor(() => expect(result.current.checking).toBe(false));
    expect(result.current.result).toBeNull();
    expect(result.current.error).toBe('DHCP endpoint unavailable');
  });
});
