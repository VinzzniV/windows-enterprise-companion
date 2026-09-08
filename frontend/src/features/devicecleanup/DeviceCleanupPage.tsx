import { useEffect, useMemo, useRef, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import type {
  ActionEvidenceAvailability,
  DeviceCleanupCandidate,
  DeviceCleanupClassification,
  DeviceCleanupPage as DeviceCleanupPageResult,
  ExportDeviceCleanupAssessmentResult,
  ProbeHostsResult,
} from '../../shared/api-types';
import {
  BridgeCancelledError,
  invoke,
  invokeCancellable,
  type CancellableBridgeInvocation,
} from '../../shared/bridge/bridgeClient';
import { errorText } from '../../shared/bridge/errorText';
import { presentError, type ErrorPresentation } from '../../shared/bridge/errorPresentation';
import { HygieneLoadStatus } from '../../shared/environment/HygieneLoadStatus';
import { useEnvironment, useEnvironmentRequest } from '../../shared/environment/EnvironmentContext';
import { useHygieneOperation } from '../../shared/environment/useHygieneOperation';
import { Badge, type BadgeTone } from '../../shared/ui/Badge';
import { Button } from '../../shared/ui/Button';
import { Checkbox } from '../../shared/ui/Checkbox';
import { DataTable, type DataColumn } from '../../shared/ui/DataTable';
import { DetailDialog } from '../../shared/ui/DetailDialog';
import { DetailsDisclosure } from '../../shared/ui/DetailsDisclosure';
import { controlClass, Input } from '../../shared/ui/Input';
import { PageHeader } from '../../shared/ui/PageHeader';
import { Select } from '../../shared/ui/Select';
import { ErrorState } from '../../shared/ui/States';
import { Toolbar } from '../../shared/ui/Toolbar';
import {
  ClientSemanticStatus,
  clientConnectivityStatus,
  type ClientConnectivityState,
} from '../clients/clientStatus';
import {
  toDeviceCleanupMarkdown,
  type DeviceCleanupDecision,
} from './deviceCleanupExport';

const classificationPresentation: Record<DeviceCleanupClassification, { label: string; tone: BadgeTone }> = {
  POTENTIAL_CLEANUP: { label: 'Potential cleanup', tone: 'fail' },
  REVIEW: { label: 'Review', tone: 'warn' },
  INSUFFICIENT_EVIDENCE: { label: 'Insufficient evidence', tone: 'neutral' },
  NO_CLEANUP_SIGNAL: { label: 'No cleanup signal', tone: 'ok' },
};

const coveragePresentation: Record<ActionEvidenceAvailability, { label: string; tone: BadgeTone }> = {
  AVAILABLE: { label: 'Available', tone: 'ok' },
  PARTIAL: { label: 'Partial', tone: 'warn' },
  NOT_CONNECTED: { label: 'Not connected', tone: 'neutral' },
  UNAVAILABLE: { label: 'Unavailable', tone: 'fail' },
  TRUNCATED: { label: 'Truncated', tone: 'warn' },
};

const decisionOptions: readonly { value: DeviceCleanupDecision; label: string }[] = [
  { value: 'KEEP', label: 'Keep device' },
  { value: 'RECHECK', label: 'Recheck evidence' },
  { value: 'PREPARE_CLEANUP', label: 'Prepare controlled cleanup' },
  { value: 'EXCLUDE', label: 'Exclude from cleanup review' },
];

function formatTimestamp(value: string | null): string {
  return value ? new Date(value).toLocaleString() : 'Not available';
}

function sourceTimestampSummary(candidate: DeviceCleanupCandidate): string {
  const timestamps = [
    candidate.activeDirectoryLastLogonAtUtc,
    candidate.kasperskyLastSeenAtUtc,
    candidate.opsiLastSeenAtUtc,
    candidate.nessusLastScanAtUtc,
    candidate.inventoryCapturedAtUtc,
  ].filter((value): value is string => value !== null);
  if (timestamps.length === 0) return 'No activity timestamp';
  return formatTimestamp(timestamps.sort().at(-1) ?? null);
}

export function DeviceCleanupPage() {
  const environment = useEnvironment();
  const invalidateEnvironment = environment.invalidate;
  const environmentRefreshRevision = environment.refreshRevision;
  const lastEnvironmentRefreshRevision = useRef(0);
  const environmentRequest = useEnvironmentRequest();
  const hygieneOperation = useHygieneOperation();
  const [searchParams, setSearchParams] = useSearchParams();
  const selectedHost = searchParams.get('host');
  const [draftSearch, setDraftSearch] = useState('');
  const [activeSearch, setActiveSearch] = useState('');
  const [includeWithoutSignals, setIncludeWithoutSignals] = useState(false);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [refreshRevision, setRefreshRevision] = useState(0);
  const forcedRevision = useRef(0);
  const requestId = useRef(0);
  const activeLoad = useRef<CancellableBridgeInvocation<DeviceCleanupPageResult> | null>(null);
  const [result, setResult] = useState<DeviceCleanupPageResult | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<ErrorPresentation | null>(null);
  const [reviewedSources, setReviewedSources] = useState<ReadonlySet<string>>(new Set());
  const [decision, setDecision] = useState<DeviceCleanupDecision | ''>('');
  const [reason, setReason] = useState('');
  const [connectivity, setConnectivity] = useState<ClientConnectivityState | null>(null);
  const connectivityRequest = useRef(0);
  const [exporting, setExporting] = useState(false);
  const [exportMessage, setExportMessage] = useState<string | null>(null);

  useEffect(() => {
    const currentRequest = ++requestId.current;
    const force = environmentRefreshRevision > lastEnvironmentRefreshRevision.current
      || refreshRevision > forcedRevision.current;
    const operationId = hygieneOperation.begin();
    setLoading(true);
    setError(null);
    const invocation = invokeCancellable<DeviceCleanupPageResult>('devicecleanup', 'listCandidates', {
      activeDirectory: environmentRequest.activeDirectory,
      kaspersky: environmentRequest.kaspersky,
      operationId,
      force,
      search: activeSearch.trim() || null,
      selectedHost,
      includeWithoutSignals,
      page,
      pageSize,
    });
    activeLoad.current = invocation;
    void invocation.promise.then((value) => {
      if (requestId.current === currentRequest) {
        if (force) {
          lastEnvironmentRefreshRevision.current = environmentRefreshRevision;
          forcedRevision.current = refreshRevision;
        }
        setResult(value);
        if (force) invalidateEnvironment(false);
      }
    }).catch((caught: unknown) => {
      if (requestId.current === currentRequest && !(caught instanceof BridgeCancelledError)) {
        setError(presentError(caught, {
          message: 'The stale-device evidence could not be computed.',
          action: 'Review source coverage and credentials, then retry.',
        }));
      }
    }).finally(() => {
      if (requestId.current === currentRequest) {
        activeLoad.current = null;
        hygieneOperation.end();
        setLoading(false);
      }
    });
    return () => invocation.cancel();
  }, [activeSearch, environmentRefreshRevision, environmentRequest, hygieneOperation.begin, hygieneOperation.end, includeWithoutSignals, invalidateEnvironment, page, pageSize, refreshRevision, selectedHost]);

  const selectedKey = result?.selectedAssessment?.candidate.subjectKey ?? null;
  useEffect(() => {
    setReviewedSources(new Set());
    setDecision('');
    setReason('');
    setConnectivity(null);
    setExportMessage(null);
  }, [selectedKey]);

  const chooseCandidate = (candidate: DeviceCleanupCandidate) => {
    const next = new URLSearchParams(searchParams);
    next.set('host', candidate.host);
    setSearchParams(next);
  };
  const closeAssessment = () => {
    const next = new URLSearchParams(searchParams);
    next.delete('host');
    setSearchParams(next);
  };

  const columns = useMemo<DataColumn<DeviceCleanupCandidate>[]>(() => [
    {
      header: 'Classification',
      cell: (candidate) => {
        const presentation = classificationPresentation[candidate.classification];
        return <Badge tone={presentation.tone}>{presentation.label}</Badge>;
      },
    },
    {
      header: 'Device',
      cell: (candidate) => <div className="flex min-w-52 flex-col gap-0.5">
        <Link className="font-medium text-accent-300 hover:text-accent-200" to={`/clients/${encodeURIComponent(candidate.host)}`}>
          {candidate.host}
        </Link>
        <span className="font-mono text-xs text-muted">{candidate.subjectKey}</span>
      </div>,
    },
    {
      header: 'Reason',
      cell: (candidate) => <div className="min-w-72 max-w-xl">
        <p className="text-sm text-slate-200">{candidate.classificationExplanation}</p>
        <p className="mt-1 text-xs text-muted">{candidate.relevantFindingCount} relevant source finding(s)</p>
      </div>,
    },
    {
      header: 'AD state',
      cell: (candidate) => candidate.activeDirectoryEnabled === null
        ? <span className="text-muted">Unknown</span>
        : <Badge tone={candidate.activeDirectoryEnabled ? 'ok' : 'neutral'}>
          {candidate.activeDirectoryEnabled ? 'Enabled' : 'Disabled'}
        </Badge>,
    },
    {
      header: 'Most recent source time',
      cell: (candidate) => <span className="text-xs text-slate-300">{sourceTimestampSummary(candidate)}</span>,
    },
    {
      header: 'Review',
      cell: (candidate) => <Button variant="secondary" onClick={() => chooseCandidate(candidate)}>
        Review evidence
      </Button>,
    },
  ], [searchParams, setSearchParams]);

  const toggleSource = (source: string) => {
    setReviewedSources((current) => {
      const next = new Set(current);
      if (next.has(source)) next.delete(source);
      else next.add(source);
      return next;
    });
  };

  const checkConnectivity = () => {
    const assessment = result?.selectedAssessment;
    if (!assessment) return;
    const current = ++connectivityRequest.current;
    setConnectivity({ kind: 'loading' });
    void invoke<ProbeHostsResult>('connectivity', 'probeHosts', { hosts: [assessment.candidate.host] })
      .then((response) => {
        if (connectivityRequest.current !== current) return;
        const probe = response.results.find((item) =>
          item.host.localeCompare(assessment.candidate.host, undefined, { sensitivity: 'accent' }) === 0)
          ?? response.results[0];
        setConnectivity(probe ? { kind: 'loaded', probe } : { kind: 'unavailable' });
      })
      .catch(() => {
        if (connectivityRequest.current === current) setConnectivity({ kind: 'failed' });
      });
  };

  const exportAssessment = () => {
    const assessment = result?.selectedAssessment;
    if (!assessment || !decision || !reason.trim()) return;
    setExporting(true);
    setExportMessage(null);
    const probe = connectivity?.kind === 'loaded' ? connectivity.probe : null;
    const markdown = toDeviceCleanupMarkdown(
      assessment,
      reviewedSources,
      decision,
      reason,
      probe,
      new Date().toISOString(),
    );
    void invoke<ExportDeviceCleanupAssessmentResult>('devicecleanup', 'exportAssessment', { markdown })
      .then((response) => setExportMessage(response.cancelled
        ? 'Export cancelled.'
        : `Exported to ${response.filePath}`))
      .catch((caught: unknown) => setExportMessage(errorText(caught)))
      .finally(() => setExporting(false));
  };

  const connectivityPresentation = connectivity ? clientConnectivityStatus(connectivity) : null;
  const assessment = result?.selectedAssessment ?? null;

  return <div className="flex flex-col gap-4">
    <PageHeader title="Device Cleanup" subtitle="Guided read-only stale-device assessment">
      <Badge tone="info">No AD writes</Badge>
      <Button variant="secondary" disabled={loading} onClick={() => setRefreshRevision((value) => value + 1)}>
        {loading ? 'Refreshing…' : 'Refresh sources'}
      </Button>
    </PageHeader>

    <section className="rounded-lg border border-warn-800/70 bg-warn-950/20 p-4" aria-labelledby="cleanup-boundary-heading">
      <h2 id="cleanup-boundary-heading" className="font-semibold text-slate-100">Assessment, not deletion</h2>
      <p className="mt-1 text-sm text-slate-300">
        WEC explains existing source facts and records your decision only for this session. It cannot disable, move or delete a computer account.
      </p>
    </section>

    {loading && result === null && <HygieneLoadStatus
      progress={hygieneOperation.progress}
      elapsedSeconds={hygieneOperation.elapsedSeconds}
      onCancel={() => activeLoad.current?.cancel()}
    />}
    {error && result === null && <ErrorState
      title="Device cleanup unavailable"
      {...error}
      controls={<Button onClick={() => setRefreshRevision((value) => value + 1)}>Retry</Button>}
    />}
    {error && result && <div className="rounded border border-fail-800 bg-fail-950/20 px-3 py-2" role="alert">
      <p className="text-sm font-medium text-fail-200">Refresh failed; the previous assessment remains visible.</p>
      <p className="mt-1 text-xs text-slate-300">{error.message}</p>
    </div>}

    {result && <>
      <DetailsDisclosure summary="Source coverage">
        <ul className="grid gap-2 sm:grid-cols-2 xl:grid-cols-3">
          {result.sources.map((source) => {
            const presentation = coveragePresentation[source.availability];
            return <li key={source.source} className="rounded border border-slate-800 bg-slate-900/50 p-2">
              <div className="flex items-center justify-between gap-2">
                <span className="text-sm font-medium text-slate-200">{source.source}</span>
                <Badge tone={presentation.tone}>{presentation.label}</Badge>
              </div>
              {source.explanation && <p className="mt-1 text-xs text-muted">{source.explanation}</p>}
            </li>;
          })}
        </ul>
      </DetailsDisclosure>

      {result.subjectsTruncated && <div className="rounded border border-warn-800 bg-warn-950/20 px-3 py-2 text-sm text-warn-200" role="status">
        The subject inventory reached its configured limit. This assessment is explicitly incomplete.
      </div>}

      <form onSubmit={(event) => { event.preventDefault(); setPage(1); setActiveSearch(draftSearch); }}>
        <Toolbar actions={<Button type="submit" variant="primary" disabled={loading}>Apply search</Button>}>
          <Input
            type="search"
            aria-label="Search cleanup candidates"
            placeholder="Device or classification reason"
            value={draftSearch}
            onChange={(event) => setDraftSearch(event.target.value)}
            className="w-80"
          />
          <Checkbox
            label="Include devices without cleanup signals"
            checked={includeWithoutSignals}
            onChange={(event) => { setPage(1); setIncludeWithoutSignals(event.target.checked); }}
          />
        </Toolbar>
      </form>

      <DataTable
        columns={columns}
        rows={result.candidates}
        getRowKey={(candidate) => candidate.subjectKey}
        emptyMessage="No stale-device candidates match this search. Review source coverage before treating the result as complete."
        loading={loading}
        stickyHeader
        pagination={{
          page: result.page,
          pageSize: result.pageSize,
          total: result.total,
          itemLabel: 'devices',
          onPageChange: setPage,
          onPageSizeChange: (value) => { setPage(1); setPageSize(value); },
        }}
      />

      {assessment && <DetailDialog
        title={`Review ${assessment.candidate.host}`}
        description={assessment.candidate.classificationExplanation}
        closeLabel="Close device review"
        onClose={closeAssessment}
        wide
      >
        <Badge tone={classificationPresentation[assessment.candidate.classification].tone}>
          {classificationPresentation[assessment.candidate.classification].label}
        </Badge>

        <div className="mt-3 grid gap-3 lg:grid-cols-2 xl:grid-cols-3" aria-label="Cleanup source evidence">
          {assessment.sources.map((source) => {
            const coverage = coveragePresentation[source.coverage];
            return <article key={source.source} className="rounded border border-slate-800 bg-slate-950/40 p-3">
              <div className="flex items-start justify-between gap-2">
                <div>
                  <h3 className="font-medium text-slate-100">{source.source}</h3>
                  <p className="mt-0.5 text-sm text-slate-300">{source.state}</p>
                </div>
                <Badge tone={coverage.tone}>{coverage.label}</Badge>
              </div>
              <p className="mt-2 text-xs text-muted">Observed: {formatTimestamp(source.observedAtUtc)}</p>
              <p className="mt-1 text-xs text-slate-400">{source.explanation}</p>
              <Checkbox
                className="mt-3 border-t border-slate-800 pt-2"
                label="Reviewed in this session"
                checked={reviewedSources.has(source.source)}
                onChange={() => toggleSource(source.source)}
              />
            </article>;
          })}
        </div>

        <div className="mt-4 grid gap-4 xl:grid-cols-2">
          <section className="rounded border border-slate-800 p-3" aria-labelledby="cleanup-user-evidence-heading">
            <div className="flex items-center justify-between gap-2">
              <h3 id="cleanup-user-evidence-heading" className="font-medium text-slate-100">User relationship evidence</h3>
              <Badge tone={assessment.userEvidenceAvailability === 'AVAILABLE' ? 'ok' : 'warn'}>
                {assessment.userEvidenceAvailability.replaceAll('_', ' ').toLowerCase()}
              </Badge>
            </div>
            <p className="mt-1 text-xs text-slate-400">{assessment.userEvidenceExplanation}</p>
            {assessment.userObservations.length === 0
              ? <p className="mt-3 text-sm text-muted">No approved user/device observation is available.</p>
              : <ul className="mt-3 divide-y divide-slate-800">
                {assessment.userObservations.map((observation) => <li key={`${observation.relationshipType}:${observation.sid}`} className="py-2 text-sm">
                  <p className="font-medium text-slate-200">{observation.accountDisplay ?? observation.sid}</p>
                  <p className="mt-0.5 text-xs text-slate-400">{observation.relationshipType} · {observation.confidence} confidence · observed {formatTimestamp(observation.observedAtUtc)}</p>
                  {observation.profileLastUseAtUtc && <p className="mt-0.5 text-xs text-muted">Profile last use: {formatTimestamp(observation.profileLastUseAtUtc)}</p>}
                  <p className="mt-1 text-xs text-slate-500">{observation.explanation}</p>
                </li>)}
              </ul>}
          </section>

          <section className="rounded border border-slate-800 p-3" aria-labelledby="cleanup-connectivity-heading">
            <h3 id="cleanup-connectivity-heading" className="font-medium text-slate-100">Current connectivity</h3>
            <p className="mt-1 text-xs text-slate-400">No probe runs when the workspace or device review opens.</p>
            <div className="mt-3 flex flex-wrap items-center gap-3">
              <Button variant="secondary" disabled={connectivity?.kind === 'loading'} onClick={checkConnectivity}>
                {connectivity?.kind === 'loading' ? 'Checking…' : 'Check Ping and WinRM'}
              </Button>
              {connectivityPresentation
                ? <ClientSemanticStatus status={connectivityPresentation.status} context={connectivityPresentation.context} />
                : <Badge tone="neutral">Not checked</Badge>}
            </div>
            <p className="mt-3 text-xs text-muted">A missing response is evidence only; it never becomes an automatic cleanup decision.</p>
          </section>
        </div>

        <section className="mt-4 rounded border border-accent-800/60 bg-accent-950/20 p-4" aria-labelledby="cleanup-decision-heading">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <h3 id="cleanup-decision-heading" className="font-medium text-slate-100">Manual session decision</h3>
            <Badge tone="info">Not persisted</Badge>
          </div>
          <div className="mt-3 grid gap-3 lg:grid-cols-[minmax(15rem,20rem)_1fr]">
            <label className="text-sm text-slate-300">
              Decision
              <Select className="mt-1" value={decision} onChange={(event) => setDecision(event.target.value as DeviceCleanupDecision | '')}>
                <option value="">Select a decision</option>
                {decisionOptions.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
              </Select>
            </label>
            <label className="text-sm text-slate-300">
              Required reason
              <textarea
                aria-label="Required decision reason"
                aria-invalid={decision !== '' && reason.trim() === '' || undefined}
                className={`${controlClass} mt-1 min-h-20 w-full resize-y`}
                value={reason}
                onChange={(event) => setReason(event.target.value)}
                placeholder="Record the evidence-based reason for this session decision"
              />
            </label>
          </div>
          <div className="mt-3 flex flex-wrap items-center gap-3">
            <Button variant="primary" disabled={!decision || !reason.trim() || exporting} onClick={exportAssessment}>
              {exporting ? 'Exporting…' : 'Export assessment'}
            </Button>
            <span className="text-xs text-muted">{reviewedSources.size} of {assessment.sources.length} source facts reviewed</span>
          </div>
          {exportMessage && <p className="mt-2 text-sm text-slate-300" role="status">{exportMessage}</p>}
        </section>

        {assessment.findings.length > 0 && <DetailsDisclosure summary={`${assessment.findings.length} hygiene finding(s)`}>
          <ul className="grid gap-2">
            {assessment.findings.map((finding) => <li key={finding.code} className="rounded border border-slate-800 px-3 py-2 text-sm text-slate-300">
              <span className="font-medium text-slate-100">{finding.code}</span> · {finding.severity} — {finding.message}
            </li>)}
          </ul>
        </DetailsDisclosure>}
      </DetailDialog>}
    </>}
  </div>;
}
