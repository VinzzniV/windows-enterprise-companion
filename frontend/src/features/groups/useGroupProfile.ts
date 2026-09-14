import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useLocation } from 'react-router-dom';
import type { GroupProfileRead, GroupProfileResult, Microsoft365Query, ObjectReference } from '../../shared/api-types.generated';
import { invokeCancellable, type CancellableBridgeInvocation } from '../../shared/bridge/bridgeClient';
import { presentError } from '../../shared/bridge/errorPresentation';
import { useTargets } from '../../shared/targets/TargetContext';
import { loadView } from '../../shared/viewCache';
import { emptyUserDirectoryEndpoint, toUserDirectoryConnection, userDirectoryViewKey, type UserDirectoryEndpoint } from '../users/users';

export function useGroupDirectoryConnection() {
  const { adminCredentials } = useTargets();
  const location = useLocation();
  const endpoint = useMemo(() => (location.state as { directoryEndpoint?: UserDirectoryEndpoint } | null)?.directoryEndpoint
    ?? loadView<UserDirectoryEndpoint>(userDirectoryViewKey) ?? emptyUserDirectoryEndpoint, [location.state]);
  return useMemo(() => toUserDirectoryConnection(endpoint, adminCredentials), [endpoint, adminCredentials]);
}

export function useGroupProfile(reference: ObjectReference) {
  const directoryConnection = useGroupDirectoryConnection();
  const connection = reference.source === 'ACTIVE_DIRECTORY' ? directoryConnection : null;
  const generation = useRef(0);
  const active = useRef<CancellableBridgeInvocation<unknown> | null>(null);
  const memberPage = useRef(1);
  const [state, setState] = useState<{ reference: ObjectReference; connection: typeof connection; data: GroupProfileResult | null; busy: boolean; error: string | null } | null>(null);
  const [now, setNow] = useState(Date.now);
  const current = state?.reference === reference && state.connection === connection ? state : null;
  const load = useCallback(async (source?: GroupProfileRead | (Microsoft365Query & { tenantId?: string }), page?: number) => {
    const own = ++generation.current;
    active.current?.cancel();
    if (page !== undefined) memberPage.current = page;
    setState(previous => ({ reference, connection, data: previous?.reference === reference && previous.connection === connection ? previous.data : null, busy: true, error: null }));
    let error: string | null = null;
    if (source && typeof source !== 'string') {
      try {
        const request = invokeCancellable('microsoft365', 'read', { ...source, refresh: true }); active.current = request; await request.promise;
      } catch (caught) { error = presentError(caught).message; }
    }
    if (own !== generation.current) return;
    try {
      const request = invokeCancellable<GroupProfileResult>('groups', 'getProfile', { reference, connection,
        read: typeof source === 'string' ? source : 'CACHED', memberPage: memberPage.current });
      active.current = request;
      const data = await request.promise;
      if (own === generation.current) setState({ reference, connection, data, busy: false, error });
    } catch (caught) {
      if (own === generation.current) setState({ reference, connection, data: null, busy: false, error: presentError(caught).message });
    } finally { if (own === generation.current) active.current = null; }
  }, [reference, connection]);
  useEffect(() => { memberPage.current = 1; void load(); return () => { generation.current++; active.current?.cancel(); }; }, [load]);
  useEffect(() => { const timer = window.setInterval(() => setNow(Date.now()), 1000); return () => window.clearInterval(timer); }, []);
  useEffect(() => {
    const data = current?.data;
    if (!data) return;
    const expiries = [data.directory?.state.retainedUntilUtc, data.directoryMembers?.state.retainedUntilUtc,
      ...data.cloud?.groupReads.map(read => read.state.retainedUntilUtc) ?? [], data.cloud?.directMembers?.state.retainedUntilUtc]
      .filter((value): value is string => Boolean(value)).map(Date.parse).filter(Number.isFinite);
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
