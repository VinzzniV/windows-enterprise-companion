import { useCallback, useState } from 'react';
import { invoke } from '../../shared/bridge/bridgeClient';
import type {
  AppInfoResponse,
  DiagnosticCategory,
  DiagnosticResult,
  DiagnosticRunResult,
  DiagnosticStatus,
  TargetRequest,
} from '../../shared/api-types';
import { StatusBadge, type StatusBadgeVariant } from '../../shared/ui/StatusBadge';
import { Spinner } from '../../shared/ui/Spinner';
import { Button } from '../../shared/ui/Button';
import { PageHeader } from '../../shared/ui/PageHeader';
import { SummaryMetric } from '../../shared/ui/SummaryMetric';
import { EmptyState, ErrorState } from '../../shared/ui/States';
import { DetailsDisclosure } from '../../shared/ui/DetailsDisclosure';
import { EvidenceList } from '../../shared/ui/EvidenceList';
import { Card } from '../../shared/ui/Card';
import {
  LOCAL_TARGET_SELECTION,
  TargetSelector,
  hostKeyOf,
  toHostList,
  toTargetRequest,
  toTargetRequestForHost,
  type TargetSelection,
} from '../../shared/targets/TargetSelector';

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

function RunSummary({ results }: { results: DiagnosticResult[] }) {
  const warningCount = countByStatus(results, 'WARNING');
  const failCount = countByStatus(results, 'FAIL');
  return (
    <div className="flex flex-wrap gap-2">
      <SummaryMetric label="Pass" value={countByStatus(results, 'PASS')} tone="success" />
      <SummaryMetric label="Warning" value={warningCount} tone={warningCount > 0 ? 'warning' : 'neutral'} />
      <SummaryMetric label="Fail" value={failCount} tone={failCount > 0 ? 'danger' : 'neutral'} />
      <SummaryMetric label="Not run" value={countByStatus(results, 'NOT_RUN')} tone="neutral" />
    </div>
  );
}

function CategorySections({ results }: { results: DiagnosticResult[] }) {
  return (
    <>
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
  );
}

type HostRunState =
  | { kind: 'running' }
  | { kind: 'done'; run: DiagnosticRunResult }
  | { kind: 'error'; message: string };

interface HostRunEntry {
  key: string;
  label: string;
  state: HostRunState;
}

/** Runs one task per item with a bounded number of parallel workers. */
async function runWithConcurrencyLimit<T>(
  items: T[],
  limit: number,
  run: (item: T) => Promise<void>,
): Promise<void> {
  const queue = [...items];
  await Promise.all(
    Array.from({ length: Math.max(1, Math.min(limit, queue.length)) }, async () => {
      for (let item = queue.shift(); item !== undefined; item = queue.shift()) {
        await run(item);
      }
    }),
  );
}

export function DiagnosticsPage() {
  const [selection, setSelection] = useState<TargetSelection>(LOCAL_TARGET_SELECTION);
  const [entries, setEntries] = useState<HostRunEntry[]>([]);

  const runForTarget = useCallback((target: TargetRequest | null): Promise<void> => {
    const key = hostKeyOf(target);
    setEntries((current) => {
      const entry: HostRunEntry = {
        key,
        label: target?.host ?? 'Local machine',
        state: { kind: 'running' },
      };
      return current.some((candidate) => candidate.key === key)
        ? current.map((candidate) => (candidate.key === key ? entry : candidate))
        : [...current, entry];
    });
    return invoke<DiagnosticRunResult>('diagnostics', 'runDiagnostics', { target }, 120_000)
      .then((run) =>
        setEntries((current) =>
          current.map((entry) =>
            entry.key === key ? { ...entry, state: { kind: 'done', run } } : entry,
          ),
        ),
      )
      .catch((error: unknown) =>
        setEntries((current) =>
          current.map((entry) =>
            entry.key === key
              ? {
                  ...entry,
                  state: {
                    kind: 'error',
                    message: error instanceof Error ? error.message : String(error),
                  },
                }
              : entry,
          ),
        ),
      );
  }, []);

  const run = useCallback(async () => {
    // Results always belong to the current selection — a new run replaces them
    setEntries([]);
    if (selection.mode === 'multiple') {
      const hosts = toHostList(selection);
      const appInfo = await invoke<AppInfoResponse>('system', 'getAppInfo').catch(() => null);
      await runWithConcurrencyLimit(hosts, appInfo?.maxParallelScans ?? 4, (host) =>
        runForTarget(toTargetRequestForHost(selection, host)),
      );
    } else {
      await runForTarget(toTargetRequest(selection));
    }
  }, [selection, runForTarget]);

  const anyRunning = entries.some((entry) => entry.state.kind === 'running');
  const singleEntry = entries.length === 1 ? entries[0] : null;

  return (
    <div className="flex flex-col gap-4">
      <PageHeader
        title="Diagnostics"
        subtitle="Read-only troubleshooting per computer — connectivity probes always measure from the WEC machine"
      >
        <Button
          variant="primary"
          onClick={() => void run()}
          disabled={
            anyRunning ||
            (selection.mode === 'remote' && selection.host.trim() === '') ||
            (selection.mode === 'multiple' && toHostList(selection).length === 0)
          }
        >
          {anyRunning ? 'Running …' : 'Run diagnostics'}
        </Button>
      </PageHeader>

      <TargetSelector
        selection={selection}
        onChange={setSelection}
        disabled={anyRunning}
        allowMultiple
      />

      {entries.length === 0 && (
        <EmptyState
          title="System diagnostics"
          message="Checks network configuration, reachability, time synchronization, services, event logs, disk space, pending reboots and update recency. Remote targets run the WMI-based checks; connectivity probes are marked as local-perspective and skipped. Results are not persisted — this is a live troubleshooting snapshot."
        />
      )}

      {singleEntry ? (
        <>
          {singleEntry.state.kind === 'running' && (
            <Spinner label={`Running diagnostics on ${singleEntry.label} …`} />
          )}
          {singleEntry.state.kind === 'error' && (
            <ErrorState title={`Error — ${singleEntry.label}`} message={singleEntry.state.message} />
          )}
          {singleEntry.state.kind === 'done' && (
            <>
              <div className="flex flex-wrap items-center gap-x-3 gap-y-1 rounded border border-slate-800 bg-slate-900/50 px-3 py-2 text-sm">
                <span className="font-medium">{singleEntry.label}</span>
                <span className="text-slate-400">
                  Run completed {new Date(singleEntry.state.run.completedAtUtc).toLocaleString()}
                </span>
              </div>
              <RunSummary results={singleEntry.state.run.results} />
              <CategorySections results={singleEntry.state.run.results} />
            </>
          )}
        </>
      ) : (
        entries.map((entry) => (
          <Card key={entry.key} title={entry.label}>
            {entry.state.kind === 'running' && <Spinner label="Running diagnostics …" />}
            {entry.state.kind === 'error' && (
              <p role="alert" className="break-words text-sm text-red-400">
                {entry.state.message}
              </p>
            )}
            {entry.state.kind === 'done' && (
              <div className="flex flex-col gap-3">
                <RunSummary results={entry.state.run.results} />
                <DetailsDisclosure
                  summary={`Show ${entry.state.run.results.length} results — completed ${new Date(entry.state.run.completedAtUtc).toLocaleString()}`}
                >
                  <div className="flex flex-col gap-4">
                    <CategorySections results={entry.state.run.results} />
                  </div>
                </DetailsDisclosure>
              </div>
            )}
          </Card>
        ))
      )}
    </div>
  );
}
