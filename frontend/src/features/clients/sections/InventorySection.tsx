import { useCallback, useEffect, useState } from 'react';
import { invoke } from '../../../shared/bridge/bridgeClient';
import { errorText } from '../../../shared/bridge/errorText';
import type { GetHardwareInfoRequest, HardwareInfoResult, TargetRequest } from '../../../shared/api-types';
import { formatSnapshotAge, SnapshotGrid } from '../../inventory/HardwareInfoPage';
import { Button } from '../../../shared/ui/Button';
import { Spinner } from '../../../shared/ui/Spinner';
import { EmptyState, ErrorState } from '../../../shared/ui/States';

type State =
  | { kind: 'idle' }
  | { kind: 'loading' }
  | { kind: 'loaded'; result: HardwareInfoResult; refreshing: boolean; refreshError: string | null }
  | { kind: 'error'; message: string };

/** Inventory section of a client: cached snapshot on open, scan on demand. */
export function InventorySection({ target }: { target: TargetRequest | null }) {
  const [state, setState] = useState<State>({ kind: 'idle' });

  const load = useCallback((forceRefresh: boolean, cacheOnly: boolean) => {
    setState((current) =>
      current.kind === 'loaded' && !cacheOnly
        ? { ...current, refreshing: true, refreshError: null }
        : { kind: 'loading' },
    );
    const payload: GetHardwareInfoRequest = { target, forceRefresh, cacheOnly };
    invoke<HardwareInfoResult>('inventory', 'getHardwareInfo', payload)
      .then((result) => setState({ kind: 'loaded', result, refreshing: false, refreshError: null }))
      .catch((error: unknown) => {
        const message = errorText(error);
        setState((current) => {
          if (cacheOnly) return { kind: 'idle' };
          if (current.kind === 'loaded') {
            return { ...current, refreshing: false, refreshError: message };
          }
          return { kind: 'error', message };
        });
      });
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
            {state.refreshing
              ? 'Refreshing — previous snapshot remains visible'
              : `${state.result.fromCache ? 'From cache' : 'Freshly captured'} — ${formatSnapshotAge(state.result.capturedAtUtc)} — ${new Date(state.result.capturedAtUtc).toLocaleString()}`}
          </span>
          <Button onClick={() => load(true, false)} disabled={state.refreshing}>
            Refresh
          </Button>
        </div>
      </div>
      {state.refreshError && (
        <p
          role="alert"
          className="rounded border border-fail-700/60 bg-fail-950/30 px-3 py-2 text-sm text-fail-300"
        >
          Refresh failed; the previous snapshot is still shown. {state.refreshError}
        </p>
      )}
      <SnapshotGrid result={state.result} target={target} />
    </div>
  );
}
