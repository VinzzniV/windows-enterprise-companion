import { useOptionalWorkingSet, useWorkingSetSessions } from '../../shared/objects/WorkingSetContext';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useLocation } from 'react-router-dom';
import type { Microsoft365Query, ObjectReference, ScopedUserProfile } from '../../shared/api-types.generated';
import { invokeCancellable, type CancellableBridgeInvocation } from '../../shared/bridge/bridgeClient';
import { presentError } from '../../shared/bridge/errorPresentation';
import { useTargets } from '../../shared/targets/TargetContext';
import { loadView } from '../../shared/viewCache';
import { emptyUserDirectoryEndpoint, toUserDirectoryConnection, userDirectoryViewKey, type UserDirectoryEndpoint } from './users';

export function userProfileExpiries(data: ScopedUserProfile) {
  const cloud = data.cloud;
  return [data.directory?.retainedUntilUtc, ...cloud?.userReads.map(read => read.state.retainedUntilUtc) ?? [],
    cloud?.tenantLicenses.state.retainedUntilUtc, cloud?.userLicenses?.state.retainedUntilUtc,
    cloud?.directGroups?.state.retainedUntilUtc, cloud?.registeredDevices?.state.retainedUntilUtc,
    ...cloud?.associatedIntune.map(read => read.state.retainedUntilUtc) ?? [], cloud?.signIn?.state.retainedUntilUtc, cloud?.registration?.state.retainedUntilUtc]
    .filter((value): value is string => typeof value === 'string').map(value => new Date(value).getTime()).filter(Number.isFinite);
}

export function useUserProfile(reference: ObjectReference, directoryScope: string | null) {
  const { adminCredentials } = useTargets();
  const location = useLocation();
  const endpoint = useMemo(() => (location.state as { directoryEndpoint?: UserDirectoryEndpoint } | null)?.directoryEndpoint
    ?? loadView<UserDirectoryEndpoint>(userDirectoryViewKey) ?? emptyUserDirectoryEndpoint, [location.state]);
  const connection = useMemo(() => toUserDirectoryConnection(endpoint, adminCredentials), [endpoint, adminCredentials]);
  const context = useMemo(() => ({ connection, directoryScope }), [connection, directoryScope]);
  const sessions = useWorkingSetSessions();
  const sessionKey = String(sessions.cloud) + ':' + sessions.directory;
  const refreshCached = useOptionalWorkingSet()?.refreshCached;
  const generation = useRef(0);
  const active = useRef<CancellableBridgeInvocation<unknown> | null>(null);
  const [state, setState] = useState<{ sessionKey: string; reference: ObjectReference; context: typeof context; data: ScopedUserProfile | null; busy: boolean; error: string | null } | null>(null);
  const [now, setNow] = useState(Date.now);
  const current = state?.reference === reference && state.context === context && state.sessionKey === sessionKey ? state : null;
  const load = useCallback(async (source?: (Microsoft365Query & { tenantId?: string }) | 'directory') => {
    const own = ++generation.current;
    active.current?.cancel();
    setState(previous => ({ reference, context, sessionKey, data: previous?.reference === reference && previous.context === context && previous.sessionKey === sessionKey ? previous.data : null, busy: true, error: null }));
    let error: string | null = null;
    if (source && source !== 'directory') {
      try {
        const action = invokeCancellable('microsoft365', 'read', { ...source, refresh: true });
        active.current = action;
        await action.promise;
      } catch (caught) { error = presentError(caught).message; }
    }
    if (generation.current !== own) return;
    try {
      const action = invokeCancellable<ScopedUserProfile>('usermanagement', 'getProfile', { reference, ...context, loadDirectoryIdentity: source === 'directory' });
      active.current = action;
      const data = await action.promise;
      if (generation.current === own) setState({ reference, context, sessionKey, data, busy: false, error });
    } catch (caught) {
      if (generation.current === own) setState({ reference, context, sessionKey, data: null, busy: false, error: presentError(caught).message });
    } finally { if (generation.current === own) { active.current = null; if (source) void refreshCached?.(); } }
  }, [reference, context, sessionKey, refreshCached]);
  const resolveLegacyUser = (location.state as { resolveLegacyUser?: boolean } | null)?.resolveLegacyUser === true;
  useEffect(() => { void load(resolveLegacyUser ? 'directory' : undefined); return () => { generation.current++; active.current?.cancel(); }; }, [load, resolveLegacyUser]);
  useEffect(() => { const timer = window.setInterval(() => setNow(Date.now()), 1000); return () => window.clearInterval(timer); }, []);
  useEffect(() => {
    if (!current?.data) return;
    const expiries = userProfileExpiries(current.data);
    if (expiries.length === 0) return;
    const timer = window.setTimeout(() => {
      setState(previous => previous ? { ...previous, data: null } : previous);
      if (!current.busy) void load();
    }, Math.max(0, Math.min(...expiries) - Date.now()) + 1);
    return () => window.clearTimeout(timer);
  }, [current?.data, current?.busy, load]);
  const cancel = () => { generation.current++; active.current?.cancel(); active.current = null;
    setState(previous => previous ? { ...previous, busy: false, error: 'Read cancelled.' } : previous); };
  return { data: current?.data ?? null, busy: current?.busy ?? true, error: current?.error ?? null, load, cancel, now };
}
