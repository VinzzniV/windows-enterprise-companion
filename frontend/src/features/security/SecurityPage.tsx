import { useCallback, useEffect, useMemo, useState } from 'react';
import { invoke, subscribe } from '../../shared/bridge/bridgeClient';
import type {
  BatchScanProgress,
  BatchScanResult,
  FindingCategory,
  FindingSeverity,
  HostScanStatus,
  LatestScanResult,
  RunBatchSecurityScanRequest,
  SecurityCheckResult,
  SecurityFinding,
  SecurityScanResult,
} from '../../shared/api-types';
import { Card } from '../../shared/ui/Card';
import { StatusBadge, type StatusBadgeVariant } from '../../shared/ui/StatusBadge';
import { Spinner } from '../../shared/ui/Spinner';
import { Button } from '../../shared/ui/Button';
import { PageHeader } from '../../shared/ui/PageHeader';
import { SummaryMetric } from '../../shared/ui/SummaryMetric';
import { EmptyState, ErrorState } from '../../shared/ui/States';
import { DetailsDisclosure } from '../../shared/ui/DetailsDisclosure';
import { EvidenceList } from '../../shared/ui/EvidenceList';
import { Select } from '../../shared/ui/Select';
import { SeverityBadge } from './SeverityBadge';
import { ScanHistory } from './ScanHistory';
import {
  LOCAL_TARGET_SELECTION,
  TargetSelector,
  hostKeyOf,
  toHostList,
  toTargetRequest,
  type TargetSelection,
} from '../../shared/targets/TargetSelector';

const hostStatusVariants: Record<HostScanStatus, StatusBadgeVariant> = {
  QUEUED: 'neutral',
  CONNECTING: 'info',
  RUNNING: 'info',
  COMPLETED: 'success',
  COMPLETED_WITH_ERRORS: 'elevation',
  FAILED: 'error',
};

function HostStatusBadge({ status }: { status: HostScanStatus }) {
  return <StatusBadge variant={hostStatusVariants[status]}>{status.replaceAll('_', ' ')}</StatusBadge>;
}

type BatchState =
  | { kind: 'idle' }
  | { kind: 'running'; statuses: Record<string, HostScanStatus> }
  | { kind: 'done'; result: BatchScanResult }
  | { kind: 'error'; message: string };

const allSeverities: FindingSeverity[] = ['CRITICAL', 'HIGH', 'MEDIUM', 'LOW', 'INFO', 'UNKNOWN'];

function categoryLabel(category: FindingCategory): string {
  return category
    .toLowerCase()
    .split('_')
    .map((word) => word.charAt(0).toUpperCase() + word.slice(1))
    .join(' ');
}

type PageState =
  | { kind: 'loading' }
  | { kind: 'loaded'; scan: SecurityScanResult | null }
  | { kind: 'error'; message: string };

function severityCount(findings: SecurityFinding[], severity: FindingSeverity): number {
  return findings.filter((finding) => finding.severity === severity).length;
}

export function FindingCard({ finding }: { finding: SecurityFinding }) {
  return (
    <li className="rounded-lg border border-slate-800 bg-slate-900 p-4">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <h3 className="text-sm font-semibold">{finding.title}</h3>
          <p className="break-words text-xs text-slate-500">{finding.affectedResource}</p>
        </div>
        <div className="flex shrink-0 items-center gap-2">
          {finding.requiredPrivilege && (
            <StatusBadge variant="elevation">Requires elevation</StatusBadge>
          )}
          <SeverityBadge severity={finding.severity} />
        </div>
      </div>
      <p className="mt-2 text-sm text-slate-300">{finding.description}</p>
      <div className="mt-3">
        <EvidenceList evidence={finding.evidence} />
      </div>
      <p className="mt-3 text-sm text-slate-400">
        <span className="font-medium text-slate-300">Recommendation: </span>
        {finding.recommendation}
      </p>
    </li>
  );
}

const checkStatusLabels: Record<SecurityCheckResult['status'], string> = {
  SUCCEEDED: 'Succeeded',
  FAILED: 'Not completed',
  REQUIRES_ELEVATION: 'Requires elevation',
  NOT_APPLICABLE: 'Not applicable',
};

export function CoverageNotes({ results }: { results: SecurityCheckResult[] }) {
  const incomplete = results.filter((result) => result.status !== 'SUCCEEDED');
  if (incomplete.length === 0) {
    return null;
  }
  return (
    <Card title={`Coverage (${incomplete.length} checks not evaluated)`}>
      <ul className="flex flex-col gap-2 text-sm">
        {incomplete.map((result) => (
          <li key={result.checkId} className="flex flex-wrap items-baseline gap-x-2 gap-y-0.5">
            <StatusBadge variant={result.status === 'REQUIRES_ELEVATION' ? 'elevation' : 'neutral'}>
              {checkStatusLabels[result.status]}
            </StatusBadge>
            <span className="font-mono text-xs text-slate-300">{result.checkId}</span>
            {result.failure && <span className="text-xs text-slate-500">{result.failure.message}</span>}
          </li>
        ))}
      </ul>
    </Card>
  );
}

/** Host + status + timestamp line every result view hangs off of. */
export function ResultContext({ scan, problemCount }: {
  scan: SecurityScanResult;
  problemCount: number;
}) {
  return (
    <div className="flex flex-wrap items-center gap-x-3 gap-y-1 rounded border border-slate-800 bg-slate-900/50 px-3 py-2 text-sm">
      <span className="font-medium">{scan.host}</span>
      <StatusBadge
        variant={
          scan.coverage.isComplete
            ? 'success'
            : scan.coverage.isKnown
              ? 'elevation'
              : 'neutral'
        }
      >
        {scan.coverage.isComplete
          ? 'COVERAGE COMPLETE'
          : scan.coverage.isKnown
            ? 'COVERAGE INCOMPLETE'
            : 'COVERAGE UNAVAILABLE'}
      </StatusBadge>
      <span className="text-slate-400">{new Date(scan.completedAtUtc).toLocaleString()}</span>
      <span className="text-slate-400">
        {problemCount} finding{problemCount === 1 ? '' : 's'}
        {scan.coverage.isKnown &&
          ` · ${scan.coverage.succeededChecks}/${scan.coverage.applicableChecks} applicable checks evaluated`}
      </span>
    </div>
  );
}

export function SeveritySummary({ problems }: { problems: SecurityFinding[] }) {
  return (
    <div className="flex flex-wrap gap-2">
      <SummaryMetric label="Critical" value={severityCount(problems, 'CRITICAL')} tone={severityCount(problems, 'CRITICAL') > 0 ? 'danger' : 'neutral'} />
      <SummaryMetric label="High" value={severityCount(problems, 'HIGH')} tone={severityCount(problems, 'HIGH') > 0 ? 'danger' : 'neutral'} />
      <SummaryMetric label="Medium" value={severityCount(problems, 'MEDIUM')} tone={severityCount(problems, 'MEDIUM') > 0 ? 'warning' : 'neutral'} />
      <SummaryMetric label="Low" value={severityCount(problems, 'LOW')} tone="neutral" />
      <SummaryMetric label="Info" value={severityCount(problems, 'INFO')} tone="neutral" />
    </div>
  );
}

function BatchHostRow({ outcome }: { outcome: BatchScanResult['hosts'][number] }) {
  const findings = outcome.scan?.findings ?? null;
  return (
    <li className="rounded border border-slate-800 bg-slate-950/50 p-3">
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
        <span className="min-w-32 font-medium">{outcome.host}</span>
        <HostStatusBadge status={outcome.status} />
        {findings && (
          <span className="flex items-center gap-1">
            {allSeverities
                .map((severity) => ({ severity, count: severityCount(findings, severity) }))
              .filter((entry) => entry.count > 0)
              .map((entry) => (
                <SeverityBadge key={entry.severity} severity={entry.severity} count={entry.count} />
              ))}
            {findings.length === 0 && outcome.scan?.coverage.isComplete && (
              <span className="text-xs text-ok-400">No findings · coverage complete</span>
            )}
            {findings.length === 0 && !outcome.scan?.coverage.isComplete && (
              <span className="text-xs text-warn-400">No findings observed · coverage incomplete</span>
            )}
          </span>
        )}
        {outcome.scan && (
          <span className="text-xs text-slate-500">
            {new Date(outcome.scan.completedAtUtc).toLocaleString()}
          </span>
        )}
      </div>
      {outcome.error && (
        <p role="alert" className="mt-2 break-words text-xs text-fail-400">
          {outcome.error.phase}: {outcome.error.code} — {outcome.error.message}
          {outcome.error.details ? ` ${outcome.error.details}` : ''}
        </p>
      )}
      {outcome.scan && findings && (findings.length > 0 || !outcome.scan.coverage.isComplete) && (
        <div className="mt-2">
          <DetailsDisclosure
            summary={`Show details (${findings.length} findings, ${outcome.scan.coverage.succeededChecks}/${outcome.scan.coverage.applicableChecks} checks evaluated)`}
          >
            <div className="flex flex-col gap-3">
              <ul className="flex flex-col gap-3">
                {findings.map((finding, index) => (
                  <FindingCard key={`${finding.findingId}-${index}`} finding={finding} />
                ))}
              </ul>
              <CoverageNotes results={outcome.scan.checkResults} />
            </div>
          </DetailsDisclosure>
        </div>
      )}
    </li>
  );
}

export function SecurityPage() {
  const [state, setState] = useState<PageState>({ kind: 'loading' });
  const [scanning, setScanning] = useState(false);
  const [selection, setSelection] = useState<TargetSelection>(LOCAL_TARGET_SELECTION);
  const [activeTarget, setActiveTarget] = useState<ReturnType<typeof toTargetRequest>>(null);
  const [hiddenSeverities, setHiddenSeverities] = useState<Set<FindingSeverity>>(new Set());
  const [categoryFilter, setCategoryFilter] = useState<FindingCategory | 'ALL'>('ALL');

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

  const availableCategories = useMemo(
    () => [...new Set(problems.map((finding) => finding.category))],
    [problems],
  );

  const visibleFindings = useMemo(
    () =>
      problems.filter(
        (finding) =>
          !hiddenSeverities.has(finding.severity) &&
          (categoryFilter === 'ALL' || finding.category === categoryFilter),
      ),
    [problems, hiddenSeverities, categoryFilter],
  );

  const toggleSeverity = (severity: FindingSeverity) => {
    setHiddenSeverities((current) => {
      const next = new Set(current);
      if (next.has(severity)) {
        next.delete(severity);
      } else {
        next.add(severity);
      }
      return next;
    });
  };

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
          ) : (
            <>
              <div className="flex flex-wrap items-center gap-2 text-xs">
                <span className="text-slate-500">Severity:</span>
                {allSeverities.map((severity) => (
                  <button
                    key={severity}
                    type="button"
                    onClick={() => toggleSeverity(severity)}
                    className={`cursor-pointer rounded border px-2 py-0.5 transition-colors ${
                      hiddenSeverities.has(severity)
                        ? 'border-slate-800 text-slate-600'
                        : 'border-slate-600 text-slate-200'
                    }`}
                  >
                    {severity}
                  </button>
                ))}
                <span className="ml-4 text-slate-500">Category:</span>
                <Select
                  fullWidth={false}
                  value={categoryFilter}
                  onChange={(event) => setCategoryFilter(event.target.value as FindingCategory | 'ALL')}
                  aria-label="Category filter"
                >
                  <option value="ALL">All</option>
                  {availableCategories.map((category) => (
                    <option key={category} value={category}>
                      {categoryLabel(category)}
                    </option>
                  ))}
                </Select>
              </div>

              <ul className="flex flex-col gap-3">
                {visibleFindings.map((finding, index) => (
                  <FindingCard key={`${finding.findingId}-${index}`} finding={finding} />
                ))}
                {visibleFindings.length === 0 && (
                  <li className="text-sm text-slate-400">All findings are hidden by the current filters.</li>
                )}
              </ul>
            </>
          )}

          <CoverageNotes results={scan.checkResults} />
        </>
      )}

      {showSingleResults && state.kind === 'loaded' && (
        <ScanHistory refreshToken={scan?.scanId ?? null} target={activeTarget} />
      )}
    </div>
  );
}
