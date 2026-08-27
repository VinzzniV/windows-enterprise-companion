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
  | { kind: 'idle' }
  | { kind: 'running' }
  | { kind: 'done'; run: DiagnosticRunResult }
  | { kind: 'error'; error: ErrorPresentation };

/** Latest stored Health snapshot with an explicit on-demand refresh. */
export function HealthSection({ target }: { target: TargetRequest | null }) {
  const [state, setState] = useState<State>({ kind: 'loading' });

  useEffect(() => {
    setState({ kind: 'loading' });
    invoke<LatestDiagnosticRunResult>('diagnostics', 'getLatestDiagnostics', { target })
      .then((result) =>
        result.run ? setState({ kind: 'done', run: result.run }) : setState({ kind: 'idle' }),
      )
      .catch(() => setState({ kind: 'idle' }));
  }, [target]);

  const run = useCallback(() => {
    setState({ kind: 'running' });
    invoke<DiagnosticRunResult>('diagnostics', 'runDiagnostics', { target })
      .then((result) => setState({ kind: 'done', run: result }))
      .catch((error: unknown) => setState({
        kind: 'error',
        error: presentError(error, { message: 'The health check could not be completed.' }),
      }));
  }, [target]);

  if (state.kind === 'loading') {
    return <Spinner label="Loading latest health snapshot …" />;
  }

  if (state.kind === 'running') {
    return <Spinner label="Running health check …" />;
  }

  if (state.kind === 'error') {
    return (
      <ErrorState
        {...state.error}
        controls={<Button onClick={run}>Retry health check</Button>}
      />
    );
  }

  if (state.kind === 'idle') {
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
        <Button onClick={run}>Re-run health check</Button>
      </div>
      <RunSummary results={state.run.results} />
      <CategorySections results={state.run.results} />
    </div>
  );
}
