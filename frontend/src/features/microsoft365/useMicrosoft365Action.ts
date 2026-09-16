import { useCallback, useEffect, useRef, useState } from 'react';
import { BridgeInvokeError, invokeCancellable } from '../../shared/bridge/bridgeClient';
import { presentError } from '../../shared/bridge/errorPresentation';

export function useMicrosoft365Action<T>() {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const current = useRef<{ cancel(): void } | null>(null);
  const generation = useRef(0);
  useEffect(() => () => { generation.current++; current.current?.cancel(); }, []);

  const run = useCallback(async (action: string, payload: unknown = {}): Promise<T | undefined> => {
    const requestGeneration = ++generation.current;
    current.current?.cancel();
    setBusy(true);
    setError(null);
    try {
      const request = invokeCancellable<T>('microsoft365', action, payload);
      current.current = request;
      const result = await request.promise;
      if (generation.current !== requestGeneration) return;
      setData(result);
      return result;
    } catch (caught) {
      if (generation.current === requestGeneration) {
        setError(caught instanceof BridgeInvokeError ? caught.error.message : presentError(caught).message);
      }
    } finally {
      if (generation.current === requestGeneration) { setBusy(false); current.current = null; }
    }
  }, []);
  const clear = useCallback(() => setData(null), []);
  return { data, error, busy, run, cancel: () => current.current?.cancel(), clear };
}
