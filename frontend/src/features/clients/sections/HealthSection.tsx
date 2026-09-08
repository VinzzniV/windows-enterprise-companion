import { useCallback, useEffect, useState } from 'react';
import { invoke } from '../../../shared/bridge/bridgeClient';
import { presentError, type ErrorPresentation } from '../../../shared/bridge/errorPresentation';
import type {
  DiagnosticRunResult,
  LatestDiagnosticRunResult,
  TargetRequest,
} from '../../../shared/api-types';
import { CategorySections, RunSummary } from '../../diagnostics/HealthResults';
import { Button } from '../../../shared/ui/Button';
import { Spinner } from '../../../shared/ui/Spinner';
import { EmptyState, ErrorState } from '../../../shared/ui/States';

type State =
  | { kind: 'loading' }
  | { kind: 'missing' }
  | { kind: 'running' }
  | { kind: 'done'; run: DiagnosticRunResult; refreshing: boolean; refreshError: ErrorPresentation | null }
  | { kind: 'readError'; error: ErrorPresentation }
  | { kind: 'runError'; error: ErrorPresentation };

/** Latest stored Health snapshot with an explicit on-demand refresh. */
export function HealthSection({ target, onDataChanged }: { target: TargetRequest | null; onDataChanged?: () => void }) {
  const [state, setState] = useState<State>({ kind: 'loading' });

  const loadLatest = useCallback(() => {
    setState({ kind: 'loading' });
    invoke<LatestDiagnosticRunResult>('diagnostics', 'getLatestDiagnostics', { target })
      .then((result) =>
        result.run
          ? setState({ kind: 'done', run: result.run, refreshing: false, refreshError: null })
          : setState({ kind: 'missing' }),
      )
      .catch((error: unknown) => setState({
        kind: 'readError',
        error: presentError(error, { message: 'The stored health snapshot could not be loaded.' }),
      }));
  }, [target]);

  useEffect(() => {
    loadLatest();
  }, [loadLatest]);

  const run = useCallback(() => {
    setState((current) => current.kind === 'done'
      ? { ...current, refreshing: true, refreshError: null }
      : { kind: 'running' });
    invoke<DiagnosticRunResult>('diagnostics', 'runDiagnostics', { target })
      .then((result) => {
        setState({ kind: 'done', run: result, refreshing: false, refreshError: null });
        onDataChanged?.();
      })
      .catch((error: unknown) => {
        const presentation = presentError(error, { message: 'The health check could not be completed.' });
        setState((current) => current.kind === 'done'
          ? { ...current, refreshing: false, refreshError: presentation }
          : { kind: 'runError', error: presentation });
      });
  }, [target, onDataChanged]);

  if (state.kind === 'loading') {
    return <Spinner label="Loading latest health snapshot …" />;
  }

  if (state.kind === 'running') {
    return <Spinner label="Running health check …" />;
  }

  if (state.kind === 'readError') {
    return (
      <ErrorState
        {...state.error}
        controls={<Button onClick={loadLatest}>Reload stored health snapshot</Button>}
      />
    );
  }

  if (state.kind === 'runError') {
    return (
      <ErrorState
        {...state.error}
        controls={<Button onClick={run}>Retry health check</Button>}
      />
    );
  }

  if (state.kind === 'missing') {
    return (
      <EmptyState
        title="Device health"
        message="Checks Windows Update age, configured services, recent Event Log errors and free disk space. Checks run only on demand and the latest result is saved per client."
        action={<Button variant="primary" onClick={run}>Run health check</Button>}
      />
    );
  }

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center justify-between gap-3 rounded border border-slate-800 bg-slate-900/50 px-3 py-2 text-sm">
        <span className="text-slate-400">
          Latest saved health check completed {new Date(state.run.completedAtUtc).toLocaleString()}
        </span>
        <Button onClick={run} disabled={state.refreshing}>
          {state.refreshing ? 'Running health check …' : 'Re-run health check'}
        </Button>
      </div>
      {state.refreshError && (
        <ErrorState
          {...state.refreshError}
          title="Health check failed; the previous saved result is still shown."
          controls={<Button onClick={run}>Retry health check</Button>}
        />
      )}
      <RunSummary results={state.run.results} />
      <CategorySections results={state.run.results} />
    </div>
  );
}
