import { useCallback, useEffect, useState } from 'react';
import { invoke } from '../../../shared/bridge/bridgeClient';
import { errorText } from '../../../shared/bridge/errorText';
import type {
  DiagnosticRunResult,
  LatestDiagnosticRunResult,
  TargetRequest,
} from '../../../shared/api-types';
import { CategorySections, RunSummary } from '../../diagnostics/DiagnosticsPage';
import { Button } from '../../../shared/ui/Button';
import { Spinner } from '../../../shared/ui/Spinner';
import { EmptyState, ErrorState } from '../../../shared/ui/States';

type State =
  | { kind: 'loading' }
  | { kind: 'idle' }
  | { kind: 'running' }
  | { kind: 'done'; run: DiagnosticRunResult }
  | { kind: 'error'; message: string };

/** Diagnostics section of a client: latest saved run on open, new runs only on demand. */
export function DiagnosticsSection({ target }: { target: TargetRequest | null }) {
  const [state, setState] = useState<State>({ kind: 'loading' });

  // Load the last saved run on open (no network scan)
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
      .catch((error: unknown) => setState({ kind: 'error', message: errorText(error) }));
  }, [target]);

  if (state.kind === 'loading') {
    return <Spinner label="Loading last diagnostics …" />;
  }

  if (state.kind === 'running') {
    return <Spinner label="Running diagnostics …" />;
  }

  if (state.kind === 'error') {
    return (
      <div className="flex flex-col gap-3">
        <ErrorState message={state.message} />
        <div>
          <Button onClick={run}>Retry</Button>
        </div>
      </div>
    );
  }

  if (state.kind === 'idle') {
    return (
      <EmptyState
        title="System diagnostics"
        message="Network, DNS, domain, time, services, event logs and system checks. Connectivity probes always measure from the WEC machine and are skipped for remote targets. The result is saved and shown again next time."
        action={<Button variant="primary" onClick={run}>Run diagnostics</Button>}
      />
    );
  }

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center justify-between gap-3 rounded border border-slate-800 bg-slate-900/50 px-3 py-2 text-sm">
        <span className="text-slate-400">
          Latest saved run completed {new Date(state.run.completedAtUtc).toLocaleString()}
        </span>
        <Button onClick={run}>Re-run</Button>
      </div>
      <RunSummary results={state.run.results} />
      <CategorySections results={state.run.results} />
    </div>
  );
}
