import { useCallback, useEffect, useState } from 'react';
import { BridgeInvokeError, invoke } from '../../../shared/bridge/bridgeClient';
import { presentError, type ErrorPresentation } from '../../../shared/bridge/errorPresentation';
import type { GetHardwareInfoRequest, HardwareInfoResult, TargetRequest } from '../../../shared/api-types';
import { formatSnapshotAge, SnapshotGrid } from '../../inventory/InventorySnapshot';
import { Button } from '../../../shared/ui/Button';
import { Spinner } from '../../../shared/ui/Spinner';
import { EmptyState, ErrorState } from '../../../shared/ui/States';

type State =
  | { kind: 'missing' }
  | { kind: 'loading'; scanning: boolean }
  | { kind: 'loaded'; result: HardwareInfoResult; refreshing: boolean; refreshError: ErrorPresentation | null }
  | { kind: 'readError'; error: ErrorPresentation }
  | { kind: 'scanError'; error: ErrorPresentation };

/** Inventory section of a client: cached snapshot on open, scan on demand. */
export function InventorySection({
  target,
  onDataChanged,
}: {
  target: TargetRequest | null;
  onDataChanged?: () => void;
}) {
  const [state, setState] = useState<State>({ kind: 'loading', scanning: false });

  const load = useCallback((forceRefresh: boolean, cacheOnly: boolean) => {
    setState((current) =>
      current.kind === 'loaded' && !cacheOnly
        ? { ...current, refreshing: true, refreshError: null }
        : { kind: 'loading', scanning: !cacheOnly },
    );
    const payload: GetHardwareInfoRequest = { target, forceRefresh, cacheOnly };
    invoke<HardwareInfoResult>('inventory', 'getHardwareInfo', payload)
      .then((result) => {
        setState({ kind: 'loaded', result, refreshing: false, refreshError: null });
        if (!cacheOnly) onDataChanged?.();
      })
      .catch((error: unknown) => {
        setState((current) => {
          if (cacheOnly && error instanceof BridgeInvokeError && error.error.code === 'NOT_FOUND') {
            return { kind: 'missing' };
          }
          if (cacheOnly) {
            return {
              kind: 'readError',
              error: presentError(error, { message: 'The stored hardware snapshot could not be loaded.' }),
            };
          }
          const presentation = presentError(error, { message: 'Hardware inventory could not be captured.' });
          if (current.kind === 'loaded') {
            return { ...current, refreshing: false, refreshError: presentation };
          }
          return { kind: 'scanError', error: presentation };
        });
      });
  }, [target, onDataChanged]);

  // Show the stored snapshot on open without hitting the network
  useEffect(() => {
    load(false, true);
  }, [load]);

  if (state.kind === 'loading') {
    return <Spinner label={state.scanning ? 'Capturing hardware inventory …' : 'Loading stored inventory …'} />;
  }

  if (state.kind === 'readError') {
    return (
      <ErrorState
        {...state.error}
        controls={<Button onClick={() => load(false, true)}>Reload stored inventory</Button>}
      />
    );
  }

  if (state.kind === 'scanError') {
    return (
      <ErrorState
        {...state.error}
        controls={<Button onClick={() => load(true, false)}>Retry scan</Button>}
      />
    );
  }

  if (state.kind === 'missing') {
    return (
      <EmptyState
        title="No inventory yet"
        message="No stored hardware snapshot for this client. The scan reads CPU, memory, disks, network, GPUs, software and BitLocker live from this client, then saves the result in WEC's local database."
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
              : `${state.result.fromCache ? 'From cache' : 'Freshly captured'} — ${formatSnapshotAge(state.result.capturedAtUtc)} — ${new Date(state.result.capturedAtUtc).toLocaleString()}. Refresh reads the client live and replaces this locally saved snapshot.`}
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
