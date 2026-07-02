import { useCallback, useState } from 'react';
import { invoke } from '../../shared/bridge/bridgeClient';
import type { DiagnosticResult, DiagnosticRunResult, DiagnosticStatus } from '../../shared/api-types';
import { Card } from '../../shared/ui/Card';
import { StatusBadge } from '../../shared/ui/StatusBadge';
import { Spinner } from '../../shared/ui/Spinner';

const statusStyles: Record<DiagnosticStatus, string> = {
  PASS: 'border-emerald-700 bg-emerald-900/60 text-emerald-300',
  WARNING: 'border-amber-700 bg-amber-900/60 text-amber-300',
  FAIL: 'border-red-700 bg-red-900/60 text-red-300',
  NOT_RUN: 'border-slate-700 bg-slate-800 text-slate-300',
};

function DiagnosticStatusBadge({ status }: { status: DiagnosticStatus }) {
  return (
    <span
      className={`inline-flex items-center rounded border px-2 py-0.5 text-xs font-medium ${statusStyles[status]}`}
    >
      {status.replace('_', ' ')}
    </span>
  );
}

function DiagnosticCard({ result }: { result: DiagnosticResult }) {
  return (
    <li className="rounded-lg border border-slate-800 bg-slate-900 p-4">
      <div className="flex items-start justify-between gap-3">
        <div>
          <h3 className="text-sm font-semibold">{result.title}</h3>
          <p className="text-xs text-slate-500">{result.affectedResource}</p>
        </div>
        <div className="flex shrink-0 items-center gap-2">
          {result.requiredPrivilege && <StatusBadge variant="elevation">Requires elevation</StatusBadge>}
          <DiagnosticStatusBadge status={result.status} />
        </div>
      </div>
      <dl className="mt-3 grid grid-cols-[auto_1fr] gap-x-4 gap-y-0.5 rounded bg-slate-950/60 p-2 text-xs">
        {Object.entries(result.evidence).map(([key, value]) => (
          <div key={key} className="contents">
            <dt className="text-slate-500">{key}</dt>
            <dd className="break-all text-slate-300">{value}</dd>
          </div>
        ))}
      </dl>
      {result.suggestedNextSteps.length > 0 && (
        <div className="mt-3">
          <p className="text-xs font-medium uppercase tracking-wide text-slate-500">Suggested next steps</p>
          <ul className="mt-1 list-disc space-y-0.5 pl-5 text-sm text-slate-300">
            {result.suggestedNextSteps.map((step, index) => (
              <li key={index}>{step}</li>
            ))}
          </ul>
        </div>
      )}
    </li>
  );
}

type PageState =
  | { kind: 'idle' }
  | { kind: 'running' }
  | { kind: 'done'; run: DiagnosticRunResult }
  | { kind: 'error'; message: string };

export function DiagnosticsPage() {
  const [state, setState] = useState<PageState>({ kind: 'idle' });

  const run = useCallback(() => {
    setState({ kind: 'running' });
    invoke<DiagnosticRunResult>('diagnostics', 'runDiagnostics')
      .then((runResult) => setState({ kind: 'done', run: runResult }))
      .catch((error: unknown) =>
        setState({ kind: 'error', message: error instanceof Error ? error.message : String(error) }),
      );
  }, []);

  return (
    <div className="flex flex-col gap-4">
      <header className="flex items-end justify-between">
        <div>
          <h1 className="text-xl font-semibold">Diagnostics</h1>
          <p className="text-sm text-slate-400">Read-only local troubleshooting</p>
        </div>
        <div className="flex items-center gap-3">
          {state.kind === 'done' && (
            <span className="text-xs text-slate-400">
              Run completed {new Date(state.run.completedAtUtc).toLocaleString()}
            </span>
          )}
          <button
            type="button"
            onClick={run}
            disabled={state.kind === 'running'}
            className="rounded bg-slate-700 px-3 py-1.5 text-sm font-medium text-slate-100 transition-colors hover:bg-slate-600 disabled:opacity-50"
          >
            {state.kind === 'running' ? 'Running …' : 'Run diagnostics'}
          </button>
        </div>
      </header>

      {state.kind === 'idle' && (
        <Card title="Network diagnostics">
          <p className="text-sm text-slate-400">
            Checks the local network configuration, default gateway reachability and DNS resolution.
            Results are not persisted — this is a live troubleshooting snapshot.
          </p>
        </Card>
      )}

      {state.kind === 'running' && <Spinner label="Running diagnostics …" />}

      {state.kind === 'error' && (
        <Card title="Error">
          <p className="text-sm text-red-400">{state.message}</p>
        </Card>
      )}

      {state.kind === 'done' && (
        <ul className="flex flex-col gap-3">
          {state.run.results.map((result, index) => (
            <DiagnosticCard key={`${result.diagnosticId}-${index}`} result={result} />
          ))}
        </ul>
      )}
    </div>
  );
}
