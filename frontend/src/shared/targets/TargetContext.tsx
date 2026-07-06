import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react';
import type { SavedTarget, SavedTargetsResult, TargetRole } from '../api-types';
import { invoke } from '../bridge/bridgeClient';
import type { CredentialValues } from './TargetSelector';

export interface SaveTargetInput {
  label: string;
  host: string;
  role: TargetRole;
  userName?: string | null;
}

export interface TargetContextValue {
  /** Saved servers/clients (host + role + optional user name), loaded from the store. */
  savedTargets: SavedTarget[];
  /** True once the first load has resolved (or failed). */
  savedTargetsReady: boolean;
  reloadSavedTargets(): Promise<void>;
  saveTarget(input: SaveTargetInput): Promise<void>;
  deleteTarget(id: number): Promise<void>;
  /**
   * Explicit credentials the user entered for a host this session, or undefined
   * when the host should be scanned as the current user. Held in memory for the
   * app session only — never persisted, never logged (ADR 0007, see ADR 0010);
   * this is what lets a client be scanned repeatedly without re-typing.
   */
  credentialsFor(host: string): CredentialValues | undefined;
  rememberCredentials(host: string, credentials: CredentialValues): void;
  forgetCredentials(host: string): void;
}

const credentialKey = (host: string) => host.trim().toUpperCase();

const TargetContext = createContext<TargetContextValue | null>(null);

export function TargetProvider({ children }: { children: ReactNode }) {
  const [savedTargets, setSavedTargets] = useState<SavedTarget[]>([]);
  const [savedTargetsReady, setSavedTargetsReady] = useState(false);
  const [credentials, setCredentials] = useState<Record<string, CredentialValues>>({});

  const reloadSavedTargets = useCallback(async () => {
    try {
      const result = await invoke<SavedTargetsResult>('targets', 'list');
      setSavedTargets(result.targets);
    } catch {
      // Bridge unavailable (browser preview) — start with no saved targets
      setSavedTargets([]);
    } finally {
      setSavedTargetsReady(true);
    }
  }, []);

  useEffect(() => {
    void reloadSavedTargets();
  }, [reloadSavedTargets]);

  const saveTarget = useCallback(async (input: SaveTargetInput) => {
    const result = await invoke<SavedTargetsResult>('targets', 'save', {
      label: input.label,
      host: input.host,
      role: input.role,
      userName: input.userName ?? null,
    });
    setSavedTargets(result.targets);
  }, []);

  const deleteTarget = useCallback(async (id: number) => {
    const result = await invoke<SavedTargetsResult>('targets', 'delete', { id });
    setSavedTargets(result.targets);
  }, []);

  const credentialsFor = useCallback(
    (host: string): CredentialValues | undefined => credentials[credentialKey(host)],
    [credentials],
  );

  const rememberCredentials = useCallback((host: string, value: CredentialValues) => {
    setCredentials((current) => ({ ...current, [credentialKey(host)]: value }));
  }, []);

  const forgetCredentials = useCallback((host: string) => {
    setCredentials((current) => {
      if (!(credentialKey(host) in current)) return current;
      const next = { ...current };
      delete next[credentialKey(host)];
      return next;
    });
  }, []);

  const value = useMemo<TargetContextValue>(
    () => ({
      savedTargets,
      savedTargetsReady,
      reloadSavedTargets,
      saveTarget,
      deleteTarget,
      credentialsFor,
      rememberCredentials,
      forgetCredentials,
    }),
    [
      savedTargets,
      savedTargetsReady,
      reloadSavedTargets,
      saveTarget,
      deleteTarget,
      credentialsFor,
      rememberCredentials,
      forgetCredentials,
    ],
  );

  return <TargetContext.Provider value={value}>{children}</TargetContext.Provider>;
}

export function useTargets(): TargetContextValue {
  const value = useContext(TargetContext);
  if (value === null) {
    throw new Error('useTargets must be used within a TargetProvider');
  }
  return value;
}
