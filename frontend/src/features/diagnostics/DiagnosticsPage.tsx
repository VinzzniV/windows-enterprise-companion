import { useCallback, useState } from 'react';
import { invoke } from '../../shared/bridge/bridgeClient';
import type {
  DiagnosticCategory,
  DiagnosticResult,
  DiagnosticRunResult,
  DiagnosticStatus,
} from '../../shared/api-types';
import { StatusBadge, type StatusBadgeVariant } from '../../shared/ui/StatusBadge';
import { Spinner } from '../../shared/ui/Spinner';
import { Button } from '../../shared/ui/Button';
import { PageHeader } from '../../shared/ui/PageHeader';
import { SummaryMetric } from '../../shared/ui/SummaryMetric';
import { EmptyState, ErrorState } from '../../shared/ui/States';
import { DetailsDisclosure } from '../../shared/ui/DetailsDisclosure';
import { EvidenceList } from '../../shared/ui/EvidenceList';

const categoryOrder: DiagnosticCategory[] = [
  'NETWORK',
  'DNS',
  'DOMAIN',
  'TIME_SYNCHRONIZATION',
  'SERVICES',
  'EVENT_LOG',
  'SYSTEM',
];

const categoryLabels: Record<DiagnosticCategory, string> = {
  NETWORK: 'Network',
  DNS: 'DNS',
  DOMAIN: 'Domain',
  TIME_SYNCHRONIZATION: 'Time',
  SERVICES: 'Services',
  EVENT_LOG: 'Event logs',
  SYSTEM: 'System',
};

const statusVariants: Record<DiagnosticStatus, StatusBadgeVariant> = {
  PASS: 'success',
  WARNING: 'elevation',
  FAIL: 'error',
  NOT_RUN: 'neutral',
};

function DiagnosticStatusBadge({ status }: { status: DiagnosticStatus }) {
  return <StatusBadge variant={statusVariants[status]}>{status.replace('_', ' ')}</StatusBadge>;
}

function countByStatus(results: DiagnosticResult[], status: DiagnosticStatus): number {
  return results.filter((result) => result.status === status).length;
}

function DiagnosticRow({ result }: { result: DiagnosticResult }) {
  // Problems put their way forward first; healthy checks collapse to one line
  const needsAttention = result.status === 'FAIL' || result.status === 'WARNING';
  const evidenceCount = Object.keys(result.evidence).length;

  return (
    <li className="rounded-lg border border-slate-800 bg-slate-900 p-3">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <h3 className="text-sm font-semibold">{result.title}</h3>
          <p className="break-words text-xs text-slate-500">{result.affectedResource}</p>
        </div>
        <div className="flex shrink-0 items-center gap-2">
          {result.requiredPrivilege && <StatusBadge variant="elevation">Requires elevation</StatusBadge>}
          <DiagnosticStatusBadge status={result.status} />
        </div>
      </div>

      {needsAttention && result.suggestedNextSteps.length > 0 && (
        <ul className="mt-2 list-disc space-y-0.5 pl-5 text-sm text-slate-300">
          {result.suggestedNextSteps.map((step, index) => (
            <li key={index}>{step}</li>
          ))}
        </ul>
      )}

      {(evidenceCount > 0 || (!needsAttention && result.suggestedNextSteps.length > 0)) && (
        <div className="mt-2">
          <DetailsDisclosure
            summary={evidenceCount > 0 ? `Evidence (${evidenceCount})` : 'Details'}
            defaultOpen={result.status === 'FAIL'}
          >
            <div className="flex flex-col gap-2">
              <EvidenceList evidence={result.evidence} />
              {!needsAttention && result.suggestedNextSteps.length > 0 && (
                <ul className="list-disc space-y-0.5 pl-5 text-sm text-slate-300">
                  {result.suggestedNextSteps.map((step, index) => (
                    <li key={index}>{step}</li>
                  ))}
                </ul>
              )}
            </div>
          </DetailsDisclosure>
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

  const results = state.kind === 'done' ? state.run.results : [];
  const warningCount = countByStatus(results, 'WARNING');
  const failCount = countByStatus(results, 'FAIL');
  const notRunCount = countByStatus(results, 'NOT_RUN');

  return (
    <div className="flex flex-col gap-4">
      <PageHeader title="Diagnostics" subtitle="Read-only troubleshooting of this machine (local only)">
        {state.kind === 'done' && (
          <span className="text-xs text-slate-400">
            Run completed {new Date(state.run.completedAtUtc).toLocaleString()}
          </span>
        )}
        <Button variant="primary" onClick={run} disabled={state.kind === 'running'}>
          {state.kind === 'running' ? 'Running …' : 'Run diagnostics'}
        </Button>
      </PageHeader>

      {state.kind === 'idle' && (
        <EmptyState
          title="System diagnostics"
          message="Checks network configuration, gateway/DNS/domain-controller reachability, time synchronization, services, event logs, disk space, pending reboots and update recency of the local machine. Results are not persisted — this is a live troubleshooting snapshot."
        />
      )}

      {state.kind === 'running' && <Spinner label="Running diagnostics …" />}

      {state.kind === 'error' && <ErrorState message={state.message} />}

      {state.kind === 'done' && (
        <>
          <div className="flex flex-wrap gap-2">
            <SummaryMetric label="Pass" value={countByStatus(results, 'PASS')} tone="success" />
            <SummaryMetric
              label="Warning"
              value={warningCount}
              tone={warningCount > 0 ? 'warning' : 'neutral'}
            />
            <SummaryMetric label="Fail" value={failCount} tone={failCount > 0 ? 'danger' : 'neutral'} />
            <SummaryMetric label="Not run" value={notRunCount} tone="neutral" />
          </div>

          {categoryOrder
            .map((category) => ({
              category,
              results: results.filter((result) => result.category === category),
            }))
            .filter((group) => group.results.length > 0)
            .map((group) => {
              const attention =
                countByStatus(group.results, 'FAIL') + countByStatus(group.results, 'WARNING');
              return (
                <section key={group.category} aria-label={categoryLabels[group.category]}>
                  <h2 className="mb-2 flex items-baseline gap-2 border-b border-slate-800 pb-1 text-sm font-medium uppercase tracking-wide text-slate-400">
                    {categoryLabels[group.category]}
                    <span className="text-xs font-normal normal-case tracking-normal text-slate-500">
                      {group.results.length} check{group.results.length === 1 ? '' : 's'}
                      {attention > 0 && ` · ${attention} need${attention === 1 ? 's' : ''} attention`}
                    </span>
                  </h2>
                  <ul className="flex flex-col gap-2">
                    {group.results.map((result, index) => (
                      <DiagnosticRow key={`${result.diagnosticId}-${index}`} result={result} />
                    ))}
                  </ul>
                </section>
              );
            })}
        </>
      )}
    </div>
  );
}
