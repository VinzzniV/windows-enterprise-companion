import { useOptionalWorkingSet, useWorkingSetSessions } from '../../shared/objects/WorkingSetContext';
import { useCallback, useEffect, useRef, useState } from 'react';
import type { DeviceProfileResult, Microsoft365Query, ObjectReference } from '../../shared/api-types.generated';
import { invokeCancellable, type CancellableBridgeInvocation } from '../../shared/bridge/bridgeClient';
import { presentError } from '../../shared/bridge/errorPresentation';
import { useEnvironmentRequest } from '../../shared/environment/EnvironmentContext';

export function useDeviceProfile(reference: ObjectReference) {
  const context = useEnvironmentRequest();
  const sessions = useWorkingSetSessions();
  const sessionKey = String(sessions.cloud) + ':' + sessions.directory;
  const refreshCached = useOptionalWorkingSet()?.refreshCached;
  const generation = useRef(0);
  const active = useRef<CancellableBridgeInvocation<unknown> | null>(null);
  const [state, setState] = useState<{ sessionKey: string; reference: ObjectReference; context: typeof context; data: DeviceProfileResult | null; busy: boolean; error: string | null } | null>(null);
  const [now, setNow] = useState(Date.now);
  const current = state?.reference === reference && state.context === context && state.sessionKey === sessionKey ? state : null;

  const load = useCallback(async (source?: (Microsoft365Query & { tenantId?: string }) | 'directory') => {
    const own = ++generation.current;
    active.current?.cancel();
    setState(previous => ({ reference, context, sessionKey, data: previous?.reference === reference && previous.context === context && previous.sessionKey === sessionKey ? previous.data : null, busy: true, error: null }));
    let sourceError: string | null = null;
    if (source && source !== 'directory') {
      try {
        const request = invokeCancellable('microsoft365', 'read', { ...source, refresh: true });
        active.current = request;
        await request.promise;
      } catch (caught) { sourceError = presentError(caught).message; }
    }
    if (generation.current !== own) return;
    try {
      const request = invokeCancellable<DeviceProfileResult>('clients', 'getProfile', { reference, ...context, loadDirectoryIdentity: source === 'directory' });
      active.current = request;
      const data = await request.promise;
      if (generation.current === own) setState({ reference, context, sessionKey, data, busy: false, error: sourceError });
    } catch (caught) {
      if (generation.current === own) setState({ reference, context, sessionKey,
        data: null, busy: false, error: presentError(caught).message });
    } finally { if (generation.current === own) { active.current = null; if (source) void refreshCached?.(); } }
  }, [reference, context, sessionKey, refreshCached]);

  useEffect(() => {
    void load();
    return () => { generation.current++; active.current?.cancel(); };
  }, [load]);
  useEffect(() => { const timer = window.setInterval(() => setNow(Date.now()), 1000); return () => window.clearInterval(timer); }, []);

  useEffect(() => {
    const data = current?.data;
    if (!data) return;
    const cloud = data.cloud;
    const expiries = [data.directory?.retainedUntilUtc,
      ...cloud?.entraReads.map(read => read.state.retainedUntilUtc) ?? [], cloud?.intune.state.retainedUntilUtc,
      ...cloud?.managedDetails.map(read => read.state.retainedUntilUtc) ?? [], cloud?.registeredOwners?.state.retainedUntilUtc]
      .filter((value): value is string => typeof value === 'string').map(value => new Date(value).getTime()).filter(Number.isFinite);
    if (expiries.length === 0) return;
    const timer = window.setTimeout(() => {
      setState(previous => previous ? { ...previous, data: null } : previous);
      void load();
    }, Math.max(0, Math.min(...expiries) - Date.now()) + 1);
    return () => window.clearTimeout(timer);
  }, [current?.data, load]);

  const cancel = () => { generation.current++; active.current?.cancel(); active.current = null; setState(previous => previous ? { ...previous, busy: false, error: 'Read cancelled.' } : previous); };
  return { data: current?.data ?? null, busy: current?.busy ?? true, error: current?.error ?? null, load, cancel, now };
}
