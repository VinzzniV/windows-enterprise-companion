import { useCallback, useEffect, useRef, useState } from 'react';
import type { ProbeHostsResponse } from '../../shared/api-types';
import { invokeCancellable, type CancellableBridgeInvocation } from '../../shared/bridge/bridgeClient';

export type ProbeState = 'not-checked' | 'checking' | 'responding' | 'no-response' | 'failed';

export function useDeviceConnectivity(host: string | null, automatic = true) {
  const active = useRef<CancellableBridgeInvocation<ProbeHostsResponse> | null>(null);
  const revision = useRef(0);
  const automaticallyCheckedHost = useRef<string | null>(null);
  const [state, setState] = useState<{ host: string | null; status: ProbeState; checkedAt: string | null }>({
    host: null, status: 'not-checked', checkedAt: null,
  });

  const check = useCallback(() => {
    if (!host) return;
    const current = ++revision.current;
    active.current?.cancel();
    setState({ host, status: 'checking', checkedAt: null });
    const request = invokeCancellable<ProbeHostsResponse>('connectivity', 'probeHosts', { hosts: [host] });
    active.current = request;
    void request.promise.then(response => {
      if (revision.current !== current) return;
      const match = response.results.find(item => item.host.toUpperCase() === host.toUpperCase());
      setState({ host, status: match?.reachable || match?.manageable ? 'responding' : 'no-response', checkedAt: new Date().toISOString() });
    }).catch(() => {
      if (revision.current === current) setState({ host, status: 'failed', checkedAt: new Date().toISOString() });
    }).finally(() => { if (revision.current === current) active.current = null; });
  }, [host]);

  useEffect(() => {
    if (host && automatic && automaticallyCheckedHost.current !== host) {
      automaticallyCheckedHost.current = host;
      check();
    }
  }, [host, automatic, check]);
  useEffect(() => () => {
    revision.current++;
    active.current?.cancel();
    active.current = null;
  }, [host]);

  return { status: state.host === host ? state.status : 'not-checked', checkedAt: state.host === host ? state.checkedAt : null, check };
}
