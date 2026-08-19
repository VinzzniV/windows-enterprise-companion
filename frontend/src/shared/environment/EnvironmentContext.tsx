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
import type { DirectoryConnectionRequest, ItHygieneResult } from '../api-types';
import { invoke } from '../bridge/bridgeClient';
import { errorText } from '../bridge/errorText';
import { useTargets } from '../targets/TargetContext';

interface EnvironmentRequest {
  activeDirectory: DirectoryConnectionRequest;
  kaspersky: { userName: string; domain: string | null; password: string } | null;
}

interface EnvironmentContextValue {
  result: ItHygieneResult | null;
  loading: boolean;
  error: string | null;
  ensureLoaded(): Promise<ItHygieneResult | null>;
  refresh(): Promise<ItHygieneResult | null>;
  invalidate(): void;
}

const EnvironmentContext = createContext<EnvironmentContextValue | null>(null);

export function EnvironmentProvider({ children }: { children: ReactNode }) {
  const targets = useTargets();
  const [result, setResult] = useState<ItHygieneResult | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const resultRef = useRef<ItHygieneResult | null>(null);
  const inFlight = useRef<Promise<ItHygieneResult | null> | null>(null);
  const generation = useRef(0);
  const previousRequest = useRef<EnvironmentRequest | null>(null);
  const admin = targets.adminCredentials;
  const ksc = targets.kasperskyCredentials ?? admin;
  const savedDc = targets.savedTargets
    .filter((target) => target.role === 'DomainController')
    .at(-1);

  const request = useMemo<EnvironmentRequest>(() => {
    const activeDirectory: DirectoryConnectionRequest = {};
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

  const invalidate = useCallback(() => {
    generation.current += 1;
    resultRef.current = null;
    inFlight.current = null;
    setResult(null);
    setError(null);
  }, []);

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
    setLoading(true);
    setError(null);
    const promise = invoke<ItHygieneResult>(
      'employeelifecycle',
      'getHygiene',
      request,
    )
      .then((next) => {
        if (generation.current === loadGeneration) {
          resultRef.current = next;
          setResult(next);
        }
        return next;
      })
      .catch((caught: unknown) => {
        if (generation.current === loadGeneration) setError(errorText(caught));
        return null;
      })
      .finally(() => {
        if (generation.current === loadGeneration) {
          inFlight.current = null;
          setLoading(false);
        }
      });
    inFlight.current = promise;
    return promise;
  }, [request]);

  const ensureLoaded = useCallback(() => load(false), [load]);
  const refresh = useCallback(() => load(true), [load]);
  const value = useMemo<EnvironmentContextValue>(() => ({
    result,
    loading,
    error,
    ensureLoaded,
    refresh,
    invalidate,
  }), [result, loading, error, ensureLoaded, refresh, invalidate]);

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
