import { useCallback, useEffect, useState } from 'react';
import { invoke } from '../../../shared/bridge/bridgeClient';
import { presentError, type ErrorPresentation } from '../../../shared/bridge/errorPresentation';
import type { GetHardwareInfoRequest, HardwareInfoResult, TargetRequest } from '../../../shared/api-types';
import { formatSnapshotAge, SnapshotGrid } from '../../inventory/HardwareInfoPage';
import { Button } from '../../../shared/ui/Button';
import { Spinner } from '../../../shared/ui/Spinner';
import { EmptyState, ErrorState } from '../../../shared/ui/States';

type State =
  | { kind: 'idle' }
  | { kind: 'loading' }
  | { kind: 'loaded'; result: HardwareInfoResult; refreshing: boolean; refreshError: ErrorPresentation | null }
  | { kind: 'error'; error: ErrorPresentation };

/** Inventory section of a client: cached snapshot on open, scan on demand. */
export function InventorySection({
  target,
  onDataChanged,
}: {
  target: TargetRequest | null;
  onDataChanged?: () => void;
}) {
  const [state, setState] = useState<State>({ kind: 'idle' });

  const load = useCallback((forceRefresh: boolean, cacheOnly: boolean) => {
    setState((current) =>
      current.kind === 'loaded' && !cacheOnly
        ? { ...current, refreshing: true, refreshError: null }
        : { kind: 'loading' },
    );
    const payload: GetHardwareInfoRequest = { target, forceRefresh, cacheOnly };
    invoke<HardwareInfoResult>('inventory', 'getHardwareInfo', payload)
      .then((result) => {
        setState({ kind: 'loaded', result, refreshing: false, refreshError: null });
        if (!cacheOnly) onDataChanged?.();
      })
      .catch((error: unknown) => {
        const presentation = presentError(error, { message: 'Hardware inventory could not be captured.' });
        setState((current) => {
          if (cacheOnly) return { kind: 'idle' };
          if (current.kind === 'loaded') {
            return { ...current, refreshing: false, refreshError: presentation };
          }
          return { kind: 'error', error: presentation };
        });
      });
  }, [target, onDataChanged]);

  // Show the stored snapshot on open without hitting the network
  useEffect(() => {
    load(false, true);
  }, [load]);

  if (state.kind === 'loading') {
    return <Spinner label="Capturing hardware inventory …" />;
  }

  if (state.kind === 'error') {
    return (
      <ErrorState
        {...state.error}
        controls={<Button onClick={() => load(true, false)}>Retry scan</Button>}
      />
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
        <ErrorState
          {...state.refreshError}
          title="Refresh failed; the previous snapshot is still shown."
          controls={<Button onClick={() => load(true, false)}>Retry refresh</Button>}
        />
      )}
      <SnapshotGrid result={state.result} target={target} />
    </div>
  );
}
