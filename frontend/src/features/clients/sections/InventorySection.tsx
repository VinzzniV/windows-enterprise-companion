import { useCallback, useEffect, useState } from 'react';
import { invoke } from '../../../shared/bridge/bridgeClient';
import { errorText } from '../../../shared/bridge/errorText';
import type { GetHardwareInfoRequest, HardwareInfoResult, TargetRequest } from '../../../shared/api-types';
import { SnapshotGrid } from '../../inventory/HardwareInfoPage';
import { Button } from '../../../shared/ui/Button';
import { Spinner } from '../../../shared/ui/Spinner';
import { EmptyState, ErrorState } from '../../../shared/ui/States';

type State =
  | { kind: 'idle' }
  | { kind: 'loading' }
  | { kind: 'loaded'; result: HardwareInfoResult }
  | { kind: 'error'; message: string };

/** Inventory section of a client: cached snapshot on open, scan on demand. */
export function InventorySection({ target }: { target: TargetRequest | null }) {
  const [state, setState] = useState<State>({ kind: 'idle' });

  const load = useCallback((forceRefresh: boolean, cacheOnly: boolean) => {
    setState({ kind: 'loading' });
    const payload: GetHardwareInfoRequest = { target, forceRefresh, cacheOnly };
    invoke<HardwareInfoResult>('inventory', 'getHardwareInfo', payload)
      .then((result) => setState({ kind: 'loaded', result }))
      .catch((error: unknown) =>
        // No cached snapshot yet is not an error — offer to scan
        cacheOnly ? setState({ kind: 'idle' }) : setState({ kind: 'error', message: errorText(error) }),
      );
  }, [target]);

  // Show the stored snapshot on open without hitting the network
  useEffect(() => {
    load(false, true);
  }, [load]);

  if (state.kind === 'loading') {
    return <Spinner label="Capturing hardware inventory …" />;
  }

  if (state.kind === 'error') {
    return (
      <div className="flex flex-col gap-3">
        <ErrorState
          message={state.message}
          hint="Check the host is reachable, WinRM is enabled and the account has remote management rights."
        />
        <div>
          <Button onClick={() => load(true, false)}>Retry scan</Button>
        </div>
      </div>
    );
  }

  if (state.kind === 'idle') {
    return (
      <EmptyState
        title="No inventory yet"
        message="No stored hardware snapshot for this client. Run a scan to capture CPU, memory, disks, network, GPUs, software and BitLocker."
        action={<Button variant="primary" onClick={() => load(false, false)}>Run inventory scan</Button>}
      />
    );
  }

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center justify-between gap-3 rounded border border-slate-800 bg-slate-900/50 px-3 py-2 text-sm">
        <span className="font-medium">{state.result.host}</span>
        <div className="flex items-center gap-3">
          <span className="text-xs text-slate-400">
            {state.result.fromCache ? 'From cache' : 'Freshly captured'} —{' '}
            {new Date(state.result.capturedAtUtc).toLocaleString()}
          </span>
          <Button onClick={() => load(true, false)}>Refresh</Button>
        </div>
      </div>
      <SnapshotGrid result={state.result} target={target} />
    </div>
  );
}
