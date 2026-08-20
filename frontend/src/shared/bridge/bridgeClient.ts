/**
 * Typed client for the WebView2 message bridge (ADR 0003).
 * Request/response correlation via envelope id, per-request timeout,
 * and subscription support for unsolicited backend events.
 */

import { bridgeResponseTimeoutMs } from './actionTimeouts';

export interface BridgeError {
  code: string;
  message: string;
  details?: string | null;
  requiredPrivilege?: string | null;
}

interface BridgeResponseEnvelope {
  id: string;
  success: boolean;
  data?: unknown;
  error?: BridgeError | null;
}

interface BridgeEventEnvelope {
  type: 'event';
  module: string;
  event: string;
  payload: unknown;
}

interface WebView2Messenger {
  postMessage(message: unknown): void;
  addEventListener(type: 'message', listener: (event: { data: unknown }) => void): void;
}

declare global {
  interface Window {
    chrome?: { webview?: WebView2Messenger };
  }
}

export class BridgeInvokeError extends Error {
  constructor(readonly error: BridgeError) {
    super(`${error.code}: ${error.message}`);
    this.name = 'BridgeInvokeError';
  }
}

export class BridgeTimeoutError extends Error {
  constructor(module: string, action: string, timeoutMs: number) {
    super(`Bridge request ${module}/${action} timed out after ${timeoutMs} ms`);
    this.name = 'BridgeTimeoutError';
  }
}

export class BridgeCancelledError extends Error {
  constructor(module: string, action: string) {
    super(`Bridge request ${module}/${action} was cancelled`);
    this.name = 'BridgeCancelledError';
  }
}

export class BridgeUnavailableError extends Error {
  constructor() {
    super('WebView2 bridge is not available; the app is not running inside the WEC host');
    this.name = 'BridgeUnavailableError';
  }
}

interface PendingRequest {
  resolve(data: unknown): void;
  reject(reason: Error): void;
  timeoutHandle: ReturnType<typeof setTimeout>;
}

export interface CancellableBridgeInvocation<TResponse> {
  requestId: string;
  promise: Promise<TResponse>;
  cancel(): void;
}

const pendingRequests = new Map<string, PendingRequest>();
const eventSubscribers = new Map<string, Set<(payload: unknown) => void>>();
let listenerAttached = false;

function eventKey(module: string, event: string): string {
  return `${module}/${event}`;
}

function getMessenger(): WebView2Messenger {
  const messenger = window.chrome?.webview;
  if (!messenger) {
    throw new BridgeUnavailableError();
  }
  return messenger;
}

function handleIncomingMessage(message: unknown): void {
  if (typeof message !== 'object' || message === null) {
    return;
  }

  if ((message as BridgeEventEnvelope).type === 'event') {
    const eventEnvelope = message as BridgeEventEnvelope;
    const subscribers = eventSubscribers.get(eventKey(eventEnvelope.module, eventEnvelope.event));
    subscribers?.forEach((callback) => callback(eventEnvelope.payload));
    return;
  }

  const response = message as BridgeResponseEnvelope;
  const pending = pendingRequests.get(response.id);
  if (!pending) {
    return;
  }

  pendingRequests.delete(response.id);
  clearTimeout(pending.timeoutHandle);

  if (response.success) {
    pending.resolve(response.data);
  } else {
    pending.reject(
      new BridgeInvokeError(
        response.error ?? { code: 'INTERNAL_ERROR', message: 'Backend returned no error details' },
      ),
    );
  }
}

function ensureListener(messenger: WebView2Messenger): void {
  if (!listenerAttached) {
    messenger.addEventListener('message', (event) => handleIncomingMessage(event.data));
    listenerAttached = true;
  }
}

export function invoke<TResponse>(
  module: string,
  action: string,
  payload?: unknown,
  timeoutOverrideMs?: number,
): Promise<TResponse> {
  return invokeCancellable<TResponse>(module, action, payload, timeoutOverrideMs).promise;
}

export function invokeCancellable<TResponse>(
  module: string,
  action: string,
  payload?: unknown,
  timeoutOverrideMs?: number,
): CancellableBridgeInvocation<TResponse> {
  // Always reject instead of throwing synchronously: callers use promise
  // .catch() paths, and a synchronous throw inside a React effect would tear
  // down the whole component tree instead of showing the page's error state.
  const id = crypto.randomUUID();
  let messenger: WebView2Messenger;
  try {
    messenger = getMessenger();
  } catch (error) {
    return {
      requestId: id,
      promise: Promise.reject(error instanceof Error ? error : new Error(String(error))),
      cancel: () => {},
    };
  }

  ensureListener(messenger);

  const timeoutMs = timeoutOverrideMs ?? bridgeResponseTimeoutMs(module, action, payload);

  const promise = new Promise<TResponse>((resolve, reject) => {
    const timeoutHandle = setTimeout(() => {
      pendingRequests.delete(id);
      messenger.postMessage({ type: 'cancel', id });
      reject(new BridgeTimeoutError(module, action, timeoutMs));
    }, timeoutMs);

    pendingRequests.set(id, {
      resolve: (data) => resolve(data as TResponse),
      reject,
      timeoutHandle,
    });

    messenger.postMessage({ id, module, action, payload: payload ?? null });
  });

  return {
    requestId: id,
    promise,
    cancel: () => {
      const pending = pendingRequests.get(id);
      if (!pending) return;
      pendingRequests.delete(id);
      clearTimeout(pending.timeoutHandle);
      messenger.postMessage({ type: 'cancel', id });
      pending.reject(new BridgeCancelledError(module, action));
    },
  };
}

export function subscribe(
  module: string,
  event: string,
  callback: (payload: unknown) => void,
): () => void {
  ensureListener(getMessenger());

  const key = eventKey(module, event);
  let subscribers = eventSubscribers.get(key);
  if (!subscribers) {
    subscribers = new Set();
    eventSubscribers.set(key, subscribers);
  }
  subscribers.add(callback);

  return () => {
    subscribers.delete(callback);
    if (subscribers.size === 0) {
      eventSubscribers.delete(key);
    }
  };
}
