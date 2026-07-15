import { afterEach } from 'vitest';
import { cleanup } from '@testing-library/react';

// Without vitest globals, Testing Library does not auto-cleanup between
// tests — stale renders accumulate and break role queries
afterEach(cleanup);

// jsdom exposes localStorage here as a bare object without the Storage methods.
// The app remembers each page's last view in it, so back it with a real in-memory
// store — otherwise every read is a silent miss and the cache can't be tested.
if (typeof globalThis.localStorage?.setItem !== 'function') {
  const store = new Map<string, string>();
  Object.defineProperty(window, 'localStorage', {
    configurable: true,
    value: {
      getItem: (key: string) => store.get(key) ?? null,
      setItem: (key: string, value: string) => void store.set(key, String(value)),
      removeItem: (key: string) => void store.delete(key),
      clear: () => store.clear(),
      key: (index: number) => [...store.keys()][index] ?? null,
      get length() {
        return store.size;
      },
    },
  });
}
