import { useCallback, useState } from 'react';
import { invoke } from '../../shared/bridge/bridgeClient';
import type {
  AppInfoResponse,
  DiagnosticRunResult,
  TargetRequest,
} from '../../shared/api-types';
import { Spinner } from '../../shared/ui/Spinner';
import { Button } from '../../shared/ui/Button';
import { PageHeader } from '../../shared/ui/PageHeader';
import { EmptyState, ErrorState } from '../../shared/ui/States';
import { Card } from '../../shared/ui/Card';
import { DetailsDisclosure } from '../../shared/ui/DetailsDisclosure';
import {
  LOCAL_TARGET_SELECTION,
  TargetSelector,
  hostKeyOf,
  toHostList,
  toTargetRequest,
  toTargetRequestForHost,
  type TargetSelection,
} from '../../shared/targets/TargetSelector';
import { runWithConcurrencyLimit } from '../../shared/concurrency';
import { CategorySections, RunSummary } from './HealthResults';

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
    return invoke<DiagnosticRunResult>('diagnostics', 'runDiagnostics', { target })
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
        title="Device Health"
        subtitle="Read-only Windows Update, service, Event Log and disk-space checks per computer"
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
          {anyRunning ? 'Running …' : 'Run health checks'}
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
          title="Device health"
          message="Checks Windows Update age, configured services, recent Event Log errors and free disk space on demand. The latest result per host is saved; no health checks run in the background."
        />
      )}

      {singleEntry ? (
        <>
          {singleEntry.state.kind === 'running' && (
            <Spinner label={`Running health checks on ${singleEntry.label} …`} />
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
            {entry.state.kind === 'running' && <Spinner label="Running health checks …" />}
            {entry.state.kind === 'error' && (
              <p role="alert" className="break-words text-sm text-fail-400">
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
