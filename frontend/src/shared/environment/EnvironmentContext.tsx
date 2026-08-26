import {
  createContext,
  useCallback,
  useContext,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react';
import type { DirectoryInventoryConnection, HygieneLoadProgress, ItHygieneRequest, ItHygieneResult } from '../api-types';
import { BridgeCancelledError, invokeCancellable, type CancellableBridgeInvocation } from '../bridge/bridgeClient';
import { presentError, type ErrorPresentation } from '../bridge/errorPresentation';
import { useTargets } from '../targets/TargetContext';
import { useHygieneOperation } from './useHygieneOperation';

interface EnvironmentContextValue {
  result: ItHygieneResult | null;
  loading: boolean;
  error: ErrorPresentation | null;
  progress: HygieneLoadProgress | null;
  elapsedSeconds: number;
  cancelled: boolean;
  ensureLoaded(): Promise<ItHygieneResult | null>;
  refresh(): Promise<ItHygieneResult | null>;
  cancel(): void;
  invalidate(): void;
}

const EnvironmentContext = createContext<EnvironmentContextValue | null>(null);

/** Connection-scoped request shared by the full environment and paged views. */
export function useEnvironmentRequest(): ItHygieneRequest {
  const targets = useTargets();
  const admin = targets.adminCredentials;
  // KSC is a separate authentication realm. Never fall back to the global
  // Windows/AD administrator; when no KSC session override is present, the
  // backend uses only the separately stored KSC credential.
  const ksc = targets.kasperskyCredentials;
  const savedDc = targets.savedTargets
    .filter((target) => target.role === 'DomainController')
    .at(-1);

  return useMemo<ItHygieneRequest>(() => {
    const activeDirectory: DirectoryInventoryConnection = {};
    if (savedDc) activeDirectory.server = savedDc.host;

    const userName = admin?.userName.trim() ?? '';
    const domain = admin?.domain.trim() ?? '';
    const isDirectoryIdentity = domain !== '' || userName.includes('\\') || userName.includes('@');
    if (admin && userName && isDirectoryIdentity) {
      activeDirectory.userName = userName;
      activeDirectory.userDomain = domain || null;
      activeDirectory.password = admin.password;
    }

    return {
      activeDirectory,
      kaspersky: ksc
        ? { userName: ksc.userName.trim(), domain: ksc.domain.trim() || null, password: ksc.password }
        : null,
    };
  }, [
    admin?.domain,
    admin?.password,
    admin?.userName,
    ksc?.domain,
    ksc?.password,
    ksc?.userName,
    savedDc?.host,
  ]);
}

export function EnvironmentProvider({ children }: { children: ReactNode }) {
  const request = useEnvironmentRequest();
  const hygieneOperation = useHygieneOperation();
  const [result, setResult] = useState<ItHygieneResult | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<ErrorPresentation | null>(null);
  const [cancelled, setCancelled] = useState(false);
  const resultRef = useRef<ItHygieneResult | null>(null);
  const inFlight = useRef<Promise<ItHygieneResult | null> | null>(null);
  const generation = useRef(0);
  const activeLoad = useRef<CancellableBridgeInvocation<ItHygieneResult> | null>(null);
  const previousRequest = useRef<ItHygieneRequest | null>(null);

  const invalidate = useCallback(() => {
    generation.current += 1;
    resultRef.current = null;
    inFlight.current = null;
    activeLoad.current?.cancel();
    activeLoad.current = null;
    hygieneOperation.end();
    setResult(null);
    setError(null);
    setCancelled(false);
  }, [hygieneOperation.end]);

  useLayoutEffect(() => {
    // The first request is the initial provider state, not a credential change. Invalidating
    // during the first effect pass can otherwise discard a load started by a child page.
    if (previousRequest.current === null) {
      previousRequest.current = request;
      return;
    }

    previousRequest.current = request;
    invalidate();
  }, [request, invalidate]);

  const load = useCallback((force: boolean) => {
    if (!force && resultRef.current) return Promise.resolve(resultRef.current);
    if (!force && inFlight.current) return inFlight.current;

    const loadGeneration = generation.current;
    const operationId = hygieneOperation.begin();
    setLoading(true);
    setError(null);
    setCancelled(false);
    const invocation = invokeCancellable<ItHygieneResult>(
      'employeelifecycle',
      'getHygiene',
      { ...request, operationId },
    );
    activeLoad.current = invocation;
    const promise = invocation.promise
      .then((next) => {
        if (generation.current === loadGeneration) {
          resultRef.current = next;
          setResult(next);
        }
        return next;
      })
      .catch((caught: unknown) => {
        if (generation.current === loadGeneration && !(caught instanceof BridgeCancelledError)) {
          setError(presentError(caught, { message: 'The shared environment data could not be loaded.' }));
        }
        return null;
      })
      .finally(() => {
        if (generation.current === loadGeneration) {
          inFlight.current = null;
          activeLoad.current = null;
          hygieneOperation.end();
          setLoading(false);
        }
      });
    inFlight.current = promise;
    return promise;
  }, [request, hygieneOperation.begin, hygieneOperation.end]);

  const ensureLoaded = useCallback(() => load(false), [load]);
  const refresh = useCallback(() => load(true), [load]);
  const cancel = useCallback(() => {
    if (!activeLoad.current) return;
    setCancelled(true);
    activeLoad.current.cancel();
  }, []);
  const value = useMemo<EnvironmentContextValue>(() => ({
    result,
    loading,
    error,
    progress: hygieneOperation.progress,
    elapsedSeconds: hygieneOperation.elapsedSeconds,
    cancelled,
    ensureLoaded,
    refresh,
    cancel,
    invalidate,
  }), [result, loading, error, hygieneOperation.progress, hygieneOperation.elapsedSeconds, cancelled, ensureLoaded, refresh, cancel, invalidate]);

  return <EnvironmentContext.Provider value={value}>{children}</EnvironmentContext.Provider>;
}

export function useEnvironment(): EnvironmentContextValue {
  const value = useContext(EnvironmentContext);
  if (!value) throw new Error('useEnvironment must be used within an EnvironmentProvider');
  return value;
}

export function useEnvironmentOptional(): EnvironmentContextValue | null {
  return useContext(EnvironmentContext);
}
