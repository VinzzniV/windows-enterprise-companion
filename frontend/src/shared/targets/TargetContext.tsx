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
import type { CredentialValues } from './Credentials';

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
   * The one admin identity for this session, or null when acting as the current
   * user. Entered once (top bar) and reused for every remote target — Windows
   * scans, AD, print servers, the PowerShell session. Held in memory for the
   * app session only: never persisted, never logged (ADR 0007, ADR 0011).
   * opsi keeps its own login (a separate auth realm).
   */
  adminCredentials: CredentialValues | null;
  signInAdmin(credentials: CredentialValues): void;
  signOutAdmin(): void;
  /** Separate KSC session override. Settings can independently save the account in Windows Credential Manager. */
  kasperskyCredentials: CredentialValues | null;
  signInKaspersky(credentials: CredentialValues): void;
  signOutKaspersky(): void;
  /**
   * Convenience for scan targets: the session admin credentials for a remote
   * host, or undefined to act as the current user. The host is accepted for
   * call-site clarity but there is a single global identity.
   */
  credentialsFor(host: string): CredentialValues | undefined;
}

const TargetContext = createContext<TargetContextValue | null>(null);

export function TargetProvider({ children }: { children: ReactNode }) {
  const [savedTargets, setSavedTargets] = useState<SavedTarget[]>([]);
  const [savedTargetsReady, setSavedTargetsReady] = useState(false);
  const [adminCredentials, setAdminCredentials] = useState<CredentialValues | null>(null);
  const [kasperskyCredentials, setKasperskyCredentials] = useState<CredentialValues | null>(null);

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

  const signInAdmin = useCallback((credentials: CredentialValues) => {
    setAdminCredentials(credentials.userName.trim() === '' ? null : credentials);
  }, []);

  const signOutAdmin = useCallback(() => setAdminCredentials(null), []);

  const signInKaspersky = useCallback((credentials: CredentialValues) => {
    setKasperskyCredentials(credentials.userName.trim() === '' ? null : credentials);
  }, []);

  const signOutKaspersky = useCallback(() => setKasperskyCredentials(null), []);

  const credentialsFor = useCallback(
    (_host: string): CredentialValues | undefined => adminCredentials ?? undefined,
    [adminCredentials],
  );

  const value = useMemo<TargetContextValue>(
    () => ({
      savedTargets,
      savedTargetsReady,
      reloadSavedTargets,
      saveTarget,
      deleteTarget,
      adminCredentials,
      signInAdmin,
      signOutAdmin,
      kasperskyCredentials,
      signInKaspersky,
      signOutKaspersky,
      credentialsFor,
    }),
    [
      savedTargets,
      savedTargetsReady,
      reloadSavedTargets,
      saveTarget,
      deleteTarget,
      adminCredentials,
      signInAdmin,
      signOutAdmin,
      kasperskyCredentials,
      signInKaspersky,
      signOutKaspersky,
      credentialsFor,
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

/** Non-throwing variant: null outside a provider. For UI that should degrade quietly. */
export function useTargetsOptional(): TargetContextValue | null {
  return useContext(TargetContext);
}
