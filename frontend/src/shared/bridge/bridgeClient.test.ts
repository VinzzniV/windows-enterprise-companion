import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

type MessageListener = (event: { data: unknown }) => void;

let listeners: MessageListener[] = [];
let postedMessages: Array<Record<string, unknown>> = [];

function installMessenger(): void {
  listeners = [];
  postedMessages = [];
  window.chrome = {
    webview: {
      postMessage: (message: unknown) => {
        postedMessages.push(message as Record<string, unknown>);
      },
      addEventListener: (_type: 'message', listener: MessageListener) => {
        listeners.push(listener);
      },
    },
  };
}

function emitFromHost(data: unknown): void {
  listeners.forEach((listener) => listener({ data }));
}

async function importBridge() {
  return import('./bridgeClient');
}

let uuidCounter = 0;

beforeEach(() => {
  // The client keeps module-level state (pending map, listener flag); isolate each test
  vi.resetModules();
  uuidCounter = 0;
  vi.stubGlobal('crypto', {
    randomUUID: () => `00000000-0000-0000-0000-${String(++uuidCounter).padStart(12, '0')}`,
  } as unknown as Crypto);
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.useRealTimers();
  delete window.chrome;
});

describe('invoke', () => {
  it('posts the request envelope and resolves with the correlated response data', async () => {
    installMessenger();
    const { invoke } = await importBridge();

    const promise = invoke<{ message: string }>('system', 'ping', { probe: true });

    expect(postedMessages).toHaveLength(1);
    const request = postedMessages[0];
    expect(request).toMatchObject({ module: 'system', action: 'ping', payload: { probe: true } });
    expect(typeof request.id).toBe('string');

    emitFromHost({ id: request.id, success: true, data: { message: 'pong' } });

    await expect(promise).resolves.toEqual({ message: 'pong' });
  });

  it('sends null payload when none is provided', async () => {
    installMessenger();
    const { invoke } = await importBridge();

    const promise = invoke('system', 'ping');

    expect(postedMessages[0].payload).toBeNull();
    emitFromHost({ id: postedMessages[0].id, success: true, data: null });
    await promise;
  });

  it('ignores responses with a foreign correlation id', async () => {
    installMessenger();
    const { invoke } = await importBridge();

    const promise = invoke<string>('system', 'ping');
    emitFromHost({ id: 'someone-elses-id', success: true, data: 'wrong' });
    emitFromHost({ id: postedMessages[0].id, success: true, data: 'right' });

    await expect(promise).resolves.toBe('right');
  });

  it('correlates concurrent requests independently', async () => {
    installMessenger();
    const { invoke } = await importBridge();

    const first = invoke<string>('system', 'ping');
    const second = invoke<string>('inventory', 'getHardwareInfo');

    // Answer in reverse order to prove correlation is by id, not arrival order
    emitFromHost({ id: postedMessages[1].id, success: true, data: 'second' });
    emitFromHost({ id: postedMessages[0].id, success: true, data: 'first' });

    await expect(first).resolves.toBe('first');
    await expect(second).resolves.toBe('second');
  });

  it('rejects with BridgeInvokeError exposing the typed error on success: false', async () => {
    installMessenger();
    const { invoke, BridgeInvokeError } = await importBridge();

    const promise = invoke('inventory', 'getDiskEncryptionStatus');
    emitFromHost({
      id: postedMessages[0].id,
      success: false,
      error: { code: 'ACCESS_DENIED', message: 'requires elevation', requiredPrivilege: 'ADMINISTRATOR' },
    });

    const error: unknown = await promise.catch((caught: unknown) => caught);
    expect(error).toBeInstanceOf(BridgeInvokeError);
    const invokeError = error as InstanceType<typeof BridgeInvokeError>;
    expect(invokeError.error.code).toBe('ACCESS_DENIED');
    expect(invokeError.error.requiredPrivilege).toBe('ADMINISTRATOR');
  });

  it('rejects with BridgeTimeoutError when no response arrives in time', async () => {
    vi.useFakeTimers();
    installMessenger();
    const { invoke, BridgeTimeoutError } = await importBridge();

    const promise = invoke('system', 'ping', undefined, 5_000);
    const assertion = expect(promise).rejects.toBeInstanceOf(BridgeTimeoutError);
    vi.advanceTimersByTime(5_001);

    await assertion;
    expect(postedMessages[1]).toEqual({ type: 'cancel', id: postedMessages[0].id });
  });

  it('cancels a correlated request once and ignores a later host response', async () => {
    installMessenger();
    const { invokeCancellable, BridgeCancelledError } = await importBridge();

    const invocation = invokeCancellable('employeelifecycle', 'getHygiene');
    const request = postedMessages[0];
    invocation.cancel();
    invocation.cancel();

    await expect(invocation.promise).rejects.toBeInstanceOf(BridgeCancelledError);
    expect(postedMessages).toEqual([
      request,
      { type: 'cancel', id: request.id },
    ]);
    emitFromHost({ id: request.id, success: true, data: { tooLate: true } });
  });

  it('does not time out a request that was already answered', async () => {
    vi.useFakeTimers();
    installMessenger();
    const { invoke } = await importBridge();

    const promise = invoke<string>('system', 'ping', undefined, 5_000);
    emitFromHost({ id: postedMessages[0].id, success: true, data: 'pong' });
    vi.advanceTimersByTime(60_000);

    await expect(promise).resolves.toBe('pong');
  });

  it('rejects with BridgeUnavailableError outside the WebView2 host', async () => {
    const { invoke, BridgeUnavailableError } = await importBridge();

    // Must reject (not throw synchronously): a synchronous throw inside a
    // React effect would unmount the tree instead of reaching .catch()
    await expect(invoke('system', 'ping')).rejects.toBeInstanceOf(BridgeUnavailableError);
  });
});

describe('subscribe', () => {
  it('delivers event payloads to matching subscribers until unsubscribed', async () => {
    installMessenger();
    const { subscribe } = await importBridge();

    const received: unknown[] = [];
    const unsubscribe = subscribe('inventory', 'scanProgress', (payload) => received.push(payload));

    emitFromHost({ type: 'event', module: 'inventory', event: 'scanProgress', payload: { percent: 40 } });
    emitFromHost({ type: 'event', module: 'inventory', event: 'otherEvent', payload: { percent: 99 } });

    expect(received).toEqual([{ percent: 40 }]);

    unsubscribe();
    emitFromHost({ type: 'event', module: 'inventory', event: 'scanProgress', payload: { percent: 80 } });

    expect(received).toEqual([{ percent: 40 }]);
  });
});
