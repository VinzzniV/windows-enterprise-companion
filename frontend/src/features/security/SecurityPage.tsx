import { useCallback, useEffect, useMemo, useState } from 'react';
import { invoke, subscribe } from '../../shared/bridge/bridgeClient';
import type {
  BatchScanProgress,
  BatchScanResult,
  HostScanStatus,
  LatestScanResult,
  RunBatchSecurityScanRequest,
  SecurityScanResult,
} from '../../shared/api-types';
import { Card } from '../../shared/ui/Card';
import { Spinner } from '../../shared/ui/Spinner';
import { Button } from '../../shared/ui/Button';
import { PageHeader } from '../../shared/ui/PageHeader';
import { EmptyState, ErrorState } from '../../shared/ui/States';
import { ScanHistory } from './ScanHistory';
import {
  LOCAL_TARGET_SELECTION,
  TargetSelector,
  hostKeyOf,
  toHostList,
  toTargetRequest,
  type TargetSelection,
} from '../../shared/targets/TargetSelector';
import {
  BatchHostRow,
  CoverageNotes,
  FindingList,
  HostStatusBadge,
  ResultContext,
  SeveritySummary,
} from './SecurityResults';

type BatchState =
  | { kind: 'idle' }
  | { kind: 'running'; statuses: Record<string, HostScanStatus> }
  | { kind: 'done'; result: BatchScanResult }
  | { kind: 'error'; message: string };

type PageState =
  | { kind: 'loading' }
  | { kind: 'loaded'; scan: SecurityScanResult | null }
  | { kind: 'error'; message: string };

export function SecurityPage() {
  const [state, setState] = useState<PageState>({ kind: 'loading' });
  const [scanning, setScanning] = useState(false);
  const [selection, setSelection] = useState<TargetSelection>(LOCAL_TARGET_SELECTION);
  const [activeTarget, setActiveTarget] = useState<ReturnType<typeof toTargetRequest>>(null);

  const loadLatest = useCallback((target: ReturnType<typeof toTargetRequest>) => {
    setActiveTarget(target);
    setState({ kind: 'loading' });
    invoke<LatestScanResult>('security', 'getLatestScan', { target })
      .then((result) => setState({ kind: 'loaded', scan: result.scan }))
      .catch((error: unknown) =>
        setState({ kind: 'error', message: error instanceof Error ? error.message : String(error) }),
      );
  }, []);

  useEffect(() => {
    loadLatest(null);
  }, [loadLatest]);

  const runScan = useCallback(() => {
    const target = toTargetRequest(selection);
    setScanning(true);
    // Displayed results always belong to the scanned target — clear the old
    // ones immediately instead of showing them next to the new selection
    setActiveTarget(target);
    setState({ kind: 'loading' });
    invoke<SecurityScanResult>('security', 'runScan', { target })
      .then((scan) => setState({ kind: 'loaded', scan }))
      .catch((error: unknown) =>
        setState({ kind: 'error', message: error instanceof Error ? error.message : String(error) }),
      )
      .finally(() => setScanning(false));
  }, [selection]);

  const [batchState, setBatchState] = useState<BatchState>({ kind: 'idle' });

  useEffect(() => {
    if (batchState.kind !== 'running') {
      return;
    }
    try {
      return subscribe('security', 'batchScanProgress', (payload) => {
        const progress = payload as BatchScanProgress;
        setBatchState((current) =>
          current.kind === 'running'
            ? { kind: 'running', statuses: { ...current.statuses, [progress.host]: progress.status } }
            : current,
        );
      });
    } catch {
      // Bridge unavailable (browser preview) — batch progress stays static
      return undefined;
    }
  }, [batchState.kind]);

  const runBatchScan = useCallback(() => {
    const hosts = toHostList(selection);
    const payload: RunBatchSecurityScanRequest = { hosts };
    if (selection.credentialMode === 'explicit' && selection.userName.trim() !== '') {
      payload.userName = selection.userName.trim();
      payload.domain = selection.domain.trim() || null;
      payload.password = selection.password;
    }
    setBatchState({
      kind: 'running',
      statuses: Object.fromEntries(hosts.map((host) => [host, 'QUEUED' as HostScanStatus])),
    });
    invoke<BatchScanResult>('security', 'runBatchScan', payload)
      .then((result) => setBatchState({ kind: 'done', result }))
      .catch((error: unknown) =>
        setBatchState({
          kind: 'error',
          message: error instanceof Error ? error.message : String(error),
        }),
      );
  }, [selection]);

  const isBatchMode = selection.mode === 'multiple';
  const batchRunning = batchState.kind === 'running';

  // Never show results for a different computer than the one selected: the
  // single-scan panels render only while selection and displayed target match
  const selectedTargetIncomplete = selection.mode === 'remote' && selection.host.trim() === '';
  const showSingleResults =
    !isBatchMode &&
    !selectedTargetIncomplete &&
    hostKeyOf(toTargetRequest(selection)) === hostKeyOf(activeTarget);

  const scan = state.kind === 'loaded' ? state.scan : null;
  const problems = useMemo(() => scan?.findings ?? [], [scan]);

  return (
    <div className="flex flex-col gap-4">
      <PageHeader title="Security" subtitle="Read-only security posture per scanned computer">
        <Button
          variant="primary"
          onClick={isBatchMode ? runBatchScan : runScan}
          disabled={
            scanning ||
            batchRunning ||
            state.kind === 'loading' ||
            selectedTargetIncomplete ||
            (isBatchMode && toHostList(selection).length === 0)
          }
        >
          {scanning || batchRunning ? 'Scanning …' : isBatchMode ? 'Scan all hosts' : 'Run scan'}
        </Button>
      </PageHeader>

      <TargetSelector
        selection={selection}
        onChange={setSelection}
        disabled={scanning || batchRunning}
        allowMultiple
      />

      {isBatchMode && batchState.kind === 'idle' && (
        <EmptyState
          title="Multi-computer scan"
          message="Enter the hosts above and start the scan. Each host is scanned independently — one unreachable computer never aborts the batch."
        />
      )}

      {isBatchMode && batchState.kind === 'running' && (
        <Card title="Batch scan in progress">
          <ul className="flex flex-col gap-1 text-sm">
            {Object.entries(batchState.statuses).map(([host, status]) => (
              <li key={host} className="flex items-center justify-between gap-3">
                <span>{host}</span>
                <HostStatusBadge status={status} />
              </li>
            ))}
          </ul>
        </Card>
      )}

      {isBatchMode && batchState.kind === 'error' && (
        <ErrorState title="Batch scan error" message={batchState.message} />
      )}

      {isBatchMode && batchState.kind === 'done' && (
        <Card title={`Batch scan results (${batchState.result.hosts.length} hosts)`}>
          <ul className="flex flex-col gap-2 text-sm">
            {batchState.result.hosts.map((outcome) => (
              <BatchHostRow key={outcome.host} outcome={outcome} />
            ))}
          </ul>
        </Card>
      )}

      {!isBatchMode && !showSingleResults && (
        <EmptyState
          title="No results for this target yet"
          message="The selection points at a different computer than the results shown before. Run a scan — or load the last saved scan for this target."
          action={
            <Button
              onClick={() => loadLatest(toTargetRequest(selection))}
              disabled={selectedTargetIncomplete || scanning}
            >
              Load last saved scan
            </Button>
          }
        />
      )}

      {showSingleResults && state.kind === 'loading' && (
        <Spinner label={scanning ? 'Scanning …' : 'Loading latest scan …'} />
      )}

      {showSingleResults && state.kind === 'error' && <ErrorState message={state.message} />}

      {showSingleResults && state.kind === 'loaded' && !scan && (
        <EmptyState
          title="No scan yet"
          message='No security scan has been run against this target. Start one with "Run scan".'
        />
      )}

      {showSingleResults && scan && (
        <>
          <ResultContext scan={scan} problemCount={problems.length} />
          <SeveritySummary problems={problems} />

          {problems.length === 0 ? (
            <Card title="Result">
              <p className={scan.coverage.isComplete ? 'text-sm text-ok-400' : 'text-sm text-warn-400'}>
                {scan.coverage.isComplete
                  ? 'No findings — all applicable checks completed.'
                  : scan.coverage.isKnown
                    ? 'No findings observed — scan coverage is incomplete.'
                    : 'No findings observed — legacy scan coverage is unavailable.'}
              </p>
            </Card>
          ) : <FindingList key={scan.scanId} findings={problems} />}

          <CoverageNotes results={scan.checkResults} />
        </>
      )}

      {showSingleResults && state.kind === 'loaded' && (
        <ScanHistory refreshToken={scan?.scanId ?? null} target={activeTarget} />
      )}
    </div>
  );
}
