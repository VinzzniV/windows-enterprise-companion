import { createContext, useCallback, useContext, useEffect, useLayoutEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { useLocation } from 'react-router-dom';
import type { ManagementDeviceObjectLists, Microsoft365ObjectLists, StoredObjectLists, WecWorkspaceIdentity } from '../api-types.generated';
import { invokeCancellable, type CancellableBridgeInvocation } from '../bridge/bridgeClient';
import { presentError } from '../bridge/errorPresentation';
import { useTargets } from '../targets/TargetContext';
import { useEnvironmentRequest } from '../environment/EnvironmentContext';
import { captureWorkingSet, type WorkingSetPolicy, type WorkingSetRead, type WorkingSetSnapshot } from './workingSet';
import { cloudWorkingSetReads, managementWorkingSetReads, storedWorkingSetReads } from './workingSetSources';

export interface WorkingDirectoryEndpoint { domain: string; server: string }
interface WorkspaceState {
  displayed: WorkingSetSnapshot | null;
  available: WorkingSetSnapshot | null;
  policy: WorkingSetPolicy | null;
  workspace: WecWorkspaceIdentity | null;
  directoryEndpoint: WorkingDirectoryEndpoint | null;
  cloudSession: number | null;
  cloudGeneration: number;
  directoryGeneration: number;
  managementGeneration: number;
  managementSession: number | null;
  managementSnapshot: string | null;
  opsiSession: string | null;
  busy: boolean;
  error: string | null;
}

interface WorkingSetContextValue extends WorkspaceState {
  hasUpdates: boolean;
  refreshCached(apply?: boolean, storedSearch?: string | null): Promise<void>;
  applyUpdates(): void;
  publishDirectory(read: WorkingSetRead, endpoint: WorkingDirectoryEndpoint): void;
  clearFamily(family: WorkingSetRead['family']): void;
}

const initial: WorkspaceState = { displayed: null, available: null, policy: null, workspace: null, directoryEndpoint: null,
  cloudSession: null, cloudGeneration: 0, directoryGeneration: 0, managementGeneration: 0, managementSession: null, managementSnapshot: null, opsiSession: null, busy: false, error: null };
const Context = createContext<WorkingSetContextValue | null>(null);

function publish(state: WorkspaceState, reads: readonly WorkingSetRead[], policy: WorkingSetPolicy, apply: boolean): WorkspaceState {
  const available = captureWorkingSet(reads, policy, Date.now());
  return { ...state, policy, available, displayed: apply || state.displayed === null ? available : state.displayed };
}

export function WorkingSetProvider({ children }: { children: ReactNode }) {
  const location = useLocation();
  const { adminCredentials } = useTargets();
  const environment = useEnvironmentRequest();
  const activeEnvironment = useRef(environment);
  const [state, setState] = useState<WorkspaceState>(initial);
  const current = useRef(state);
  const requests = useRef<CancellableBridgeInvocation<unknown>[]>([]);
  const generation = useRef(0);
  const credentials = useRef(adminCredentials);
  const update = useCallback((change: (previous: WorkspaceState) => WorkspaceState) => {
    current.current = change(current.current);
    setState(current.current);
  }, []);
  const cancelReads = useCallback(() => { generation.current++; requests.current.forEach(request => request.cancel()); requests.current = []; }, []);

  const clearFamily = useCallback((family: WorkingSetRead['family']) => {
    cancelReads();
    update(previous => {
      const policy = previous.policy ? { ...previous.policy, ...(family === 'cloud' ? { tenantId: null } : {}),
        ...(family === 'directory' ? { directoryScope: null } : {}) } : null;
      const reads = previous.available?.reads.filter(read => read.family !== family) ?? [];
      const next = { ...previous, busy: false, error: null,
        cloudGeneration: previous.cloudGeneration + (family === 'cloud' ? 1 : 0),
        directoryGeneration: previous.directoryGeneration + (family === 'directory' ? 1 : 0),
        managementGeneration: previous.managementGeneration + (family === 'management' ? 1 : 0),
        managementSession: family === 'management' ? null : previous.managementSession,
        managementSnapshot: family === 'management' ? null : previous.managementSnapshot,
        opsiSession: family === 'management' ? null : previous.opsiSession,
        directoryEndpoint: family === 'directory' ? null : previous.directoryEndpoint,
        cloudSession: family === 'cloud' ? null : previous.cloudSession };
      return policy ? publish(next, reads, policy, true) : next;
    });
  }, [cancelReads, update]);

  const refreshCached = useCallback(async (apply = false, storedSearch: string | null = null) => {
    cancelReads();
    const own = generation.current;
    update(previous => ({ ...previous, busy: true, error: null }));
    let local: CancellableBridgeInvocation<StoredObjectLists>;
    let cloud: CancellableBridgeInvocation<Microsoft365ObjectLists>;
    let management: CancellableBridgeInvocation<ManagementDeviceObjectLists>;
    try {
      local = invokeCancellable<StoredObjectLists>('clients', 'getStoredObjectLists', storedSearch ? { search: storedSearch } : undefined);
      requests.current = [local];
      void local.promise.catch(() => undefined);
      cloud = invokeCancellable<Microsoft365ObjectLists>('microsoft365', 'getCachedObjectLists');
      requests.current.push(cloud);
      void cloud.promise.catch(() => undefined);
      management = invokeCancellable<ManagementDeviceObjectLists>('clients', 'getCachedManagementObjectLists', { ...activeEnvironment.current, search: storedSearch });
      requests.current.push(management);
    } catch (caught) {
      if (own === generation.current) update(previous => ({ ...previous, busy: false, error: presentError(caught).message }));
      cancelReads();
      return;
    }
    const results = await Promise.allSettled([local.promise, cloud.promise, management.promise]);
    if (own !== generation.current) return;
    requests.current = [];
    update(previous => {
      const stored = results[0].status === 'fulfilled' ? results[0].value : null;
      const microsoft365 = results[1].status === 'fulfilled' ? results[1].value : null;
      const managed = results[2].status === 'fulfilled' ? results[2].value : null;
      const failure = results.find(result => result.status === 'rejected');
      const error = failure?.status === 'rejected' ? presentError(failure.reason).message : null;
      const scopeChanged = stored !== null && previous.workspace !== null && stored.workspace.scope !== previous.workspace.scope;
      const cloudChanged = microsoft365 !== null && previous.policy !== null && (microsoft365.sessionRevision !== previous.cloudSession
        || microsoft365.tenantId !== previous.policy?.tenantId);
      const managementChanged = managed !== null && previous.managementSession !== null
        && (managed.sessionRevision !== previous.managementSession || managed.opsiSessionId !== previous.opsiSession);
      const managementSnapshotChanged = managed !== null && managed.snapshotId !== previous.managementSnapshot;
      const tenantId = microsoft365 !== null ? microsoft365.tenantId : previous.policy?.tenantId ?? null;
      const policy = stored ? { maximumRecords: stored.maximumRecords, maximumSourceReads: stored.maximumSourceReads,
        directoryScope: scopeChanged ? null : previous.policy?.directoryScope ?? null, tenantId }
        : previous.policy ? { ...previous.policy, tenantId } : null;
      if (!policy) return { ...previous, busy: false, error: error ?? 'The working-set policy could not be loaded.' };
      const retained = scopeChanged ? [] : previous.available?.reads.filter(read => (read.family !== 'cloud' || microsoft365 === null)
        && (read.family !== 'stored' || stored === null || read.collectionKey !== JSON.stringify(['stored', stored.search]))
        && (read.family !== 'management' || managed === null || !managementChanged && !managementSnapshotChanged && read.collectionKey !== JSON.stringify(['management', managed.search]))) ?? [];
      const reads = [...retained, ...(stored ? storedWorkingSetReads(stored) : []), ...(microsoft365 ? cloudWorkingSetReads(microsoft365) : []),
        ...(managed ? managementWorkingSetReads(managed) : [])];
      const next = { ...previous, workspace: stored?.workspace ?? previous.workspace, cloudSession: microsoft365?.sessionRevision ?? previous.cloudSession,
        cloudGeneration: previous.cloudGeneration + (cloudChanged ? 1 : 0), directoryGeneration: previous.directoryGeneration + (scopeChanged ? 1 : 0),
        managementGeneration: previous.managementGeneration + (managementChanged || scopeChanged ? 1 : 0),
        managementSession: managed?.sessionRevision ?? previous.managementSession, opsiSession: managed ? managed.opsiSessionId : previous.opsiSession,
        managementSnapshot: managed ? managed.snapshotId : previous.managementSnapshot,
        directoryEndpoint: scopeChanged ? null : previous.directoryEndpoint, busy: false, error };
      return publish(next, reads, policy, apply || cloudChanged || scopeChanged || managementChanged);
    });
  }, [cancelReads, update]);

  const publishDirectory = useCallback((read: WorkingSetRead, endpoint: WorkingDirectoryEndpoint) => {
    update(previous => {
      if (!previous.policy) return previous;
      const changed = previous.directoryEndpoint !== null && (previous.directoryEndpoint.domain !== endpoint.domain || previous.directoryEndpoint.server !== endpoint.server);
      const retained = previous.available?.reads.filter(source => (!changed || source.family !== 'directory')
        && source.key !== read.key && (source.family !== 'directory' || source.title !== read.title || source.collectionKey === read.collectionKey)) ?? [];
      const policy = { ...previous.policy, directoryScope: read.scope };
      return publish({ ...previous, directoryEndpoint: endpoint, directoryGeneration: previous.directoryGeneration + (changed ? 1 : 0) }, [...retained, read], policy, true);
    });
  }, [update]);

  useLayoutEffect(() => {
    if (credentials.current === adminCredentials) return;
    credentials.current = adminCredentials;
    clearFamily('directory');
  }, [adminCredentials, clearFamily]);

  useLayoutEffect(() => {
    if (activeEnvironment.current === environment) return;
    activeEnvironment.current = environment;
    clearFamily('management');
  }, [environment, clearFamily]);

  useEffect(() => { void refreshCached(); return cancelReads; }, [location.pathname, environment, refreshCached, cancelReads]);

  useEffect(() => {
    const deadlines = [...state.available?.reads ?? [], ...state.displayed?.reads ?? []]
      .map(read => read.retainedUntilUtc === null ? NaN : Date.parse(read.retainedUntilUtc)).filter(value => Number.isFinite(value) && value > Date.now());
    if (deadlines.length === 0) return;
    const timer = window.setTimeout(() => update(previous => {
      if (!previous.policy) return previous;
      return { ...previous, displayed: previous.displayed ? captureWorkingSet(previous.displayed.reads, previous.policy, Date.now()) : null,
        available: previous.available ? captureWorkingSet(previous.available.reads, previous.policy, Date.now()) : null };
    }), Math.min(2_147_483_647, Math.max(1, Math.min(...deadlines) - Date.now() + 1)));
    return () => window.clearTimeout(timer);
  }, [state.available, state.displayed, update]);

  const applyUpdates = useCallback(() => update(previous => ({ ...previous, displayed: previous.available })), [update]);
  const value = useMemo<WorkingSetContextValue>(() => ({ ...state,
    hasUpdates: state.available !== null && state.available.revision !== state.displayed?.revision,
    refreshCached, applyUpdates, publishDirectory, clearFamily }), [state, refreshCached, applyUpdates, publishDirectory, clearFamily]);
  return <Context.Provider value={value}>{children}</Context.Provider>;
}

export function useWorkingSet(): WorkingSetContextValue {
  const value = useContext(Context);
  if (!value) throw new Error('WorkingSetProvider is required.');
  return value;
}

export function useWorkingSetSessions() {
  const value = useContext(Context);
  return { cloud: value?.cloudGeneration ?? 0, directory: value?.directoryGeneration ?? 0, management: value?.managementGeneration ?? 0 };
}

export function useOptionalWorkingSet() { return useContext(Context); }
