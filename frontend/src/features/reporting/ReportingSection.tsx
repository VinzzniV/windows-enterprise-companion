import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { invoke } from '../../shared/bridge/bridgeClient';
import type {
  ExportReportRequest,
  ReportExportResult,
  ReportOverview,
  ReportOverviewRequest,
} from '../../shared/api-types';
import { Card } from '../../shared/ui/Card';
import { Spinner } from '../../shared/ui/Spinner';
import { Button } from '../../shared/ui/Button';
import { ErrorState } from '../../shared/ui/States';
import { Checkbox } from '../../shared/ui/Checkbox';
import { SemanticStatusBadge, type SemanticStatus } from '../../shared/ui/SemanticStatusBadge';
import { presentError, type ErrorPresentation } from '../../shared/bridge/errorPresentation';

type OverviewState =
  | { kind: 'loading' }
  | { kind: 'loaded'; overview: ReportOverview }
  | { kind: 'error'; error: ErrorPresentation };

type ExportState =
  | { kind: 'idle' }
  | { kind: 'exporting' }
  | { kind: 'exported'; filePath: string; openError: ErrorPresentation | null }
  | { kind: 'cancelled' }
  | { kind: 'error'; error: ErrorPresentation };

export function formatDataAge(ageSeconds: number | null): string {
  if (ageSeconds === null) return 'Unavailable';
  const minutes = Math.floor(Math.max(0, ageSeconds) / 60);
  const days = Math.floor(minutes / (24 * 60));
  const hours = Math.floor((minutes % (24 * 60)) / 60);
  if (days > 0) return `${days}d ${hours}h old`;
  if (hours > 0) return `${hours}h ${minutes % 60}m old`;
  return `${minutes}m old`;
}

function reportSourceStatus(state: ReportOverview['readiness']['sources'][number]['state']): SemanticStatus {
  switch (state) {
    case 'READY': return { dimension: 'freshness', value: 'fresh' };
    case 'MISSING': return { dimension: 'availability', value: 'missing' };
    case 'STALE': return { dimension: 'freshness', value: 'stale' };
    case 'INCOMPLETE': return { dimension: 'execution', value: 'partial' };
    default: return { dimension: 'availability', value: 'unknown' };
  }
}

function reportReadinessStatus(isReady: boolean): SemanticStatus {
  return isReady
    ? { dimension: 'availability', value: 'available' }
    : { dimension: 'execution', value: 'partial' };
}

/**
 * Executive-summary overview + export for one machine. `host === null` targets
 * the local machine; a host string targets a scanned remote client. The report
 * reads whatever inventory/security data was already captured for that host —
 * it never triggers a live scan.
 */
export function ReportingSection({
  host,
  refreshKey = 0,
}: {
  host: string | null;
  refreshKey?: number;
}) {
  const [overviewState, setOverviewState] = useState<OverviewState>({ kind: 'loading' });
  const [exportState, setExportState] = useState<ExportState>({ kind: 'idle' });
  const [openAfterExport, setOpenAfterExport] = useState(true);

  const subject = host === null ? 'this machine' : host;

  useEffect(() => {
    let active = true;
    setOverviewState({ kind: 'loading' });
    const payload: ReportOverviewRequest = { host };
    invoke<ReportOverview>('reporting', 'getOverview', payload)
      .then((overview) => {
        if (active) setOverviewState({ kind: 'loaded', overview });
      })
      .catch((error: unknown) => {
        if (active) {
          setOverviewState({
            kind: 'error',
            error: presentError(error, { message: 'The report overview could not be loaded.' }),
          });
        }
      });
    return () => {
      active = false;
    };
  }, [host, refreshKey]);

  const exportReport = useCallback(
    (action: 'exportHtml' | 'exportJson') => {
      setExportState({ kind: 'exporting' });
      const payload: ExportReportRequest = { openAfterExport, host };
      invoke<ReportExportResult>('reporting', action, payload)
        .then((result) =>
          setExportState(
            result.cancelled || result.filePath === null
              ? { kind: 'cancelled' }
              : {
                  kind: 'exported',
                  filePath: result.filePath,
                  openError: result.openError
                    ? presentError(new Error(result.openError), {
                        message: 'The saved report could not be opened.',
                        cause: 'Windows could not open the exported file with its associated application.',
                        action: `Open ${result.filePath} manually or check the default application for this file type.`,
                      })
                    : null,
                },
          ),
        )
        .catch((error: unknown) => setExportState({
          kind: 'error',
          error: presentError(error, { message: 'The report could not be exported.' }),
        }));
    },
    [openAfterExport, host],
  );

  if (overviewState.kind === 'loading') {
    return <Spinner label="Loading overview …" />;
  }
  if (overviewState.kind === 'error') {
    return <ErrorState title="Report overview unavailable" {...overviewState.error} />;
  }

  const { overview } = overviewState;
  const subjectPath = `/clients/${encodeURIComponent(overview.subjectHost)}`;
  const sourceLinks = {
    inventory: `${subjectPath}?section=inventory`,
    security: `${subjectPath}?section=security`,
  };
  const hasAnyData =
    overview.inventoryCapturedAtUtc !== null || overview.securityScanCompletedAtUtc !== null;

  return (
    <div className="flex flex-col gap-4">
      <Card title="Report readiness">
        <div className="flex flex-col gap-3">
          <div className="flex items-center gap-3">
            <SemanticStatusBadge status={reportReadinessStatus(overview.readiness.isReady)} />
            <p className="text-sm text-slate-300">
              {overview.readiness.isReady
                ? 'All report sources are current and complete.'
                : 'One or more report sources are missing, stale, or incomplete.'}
            </p>
          </div>
          <ul className="grid gap-2 xl:grid-cols-2">
            {overview.readiness.sources.map((source) => {
              const sourceLink = source.source === 'Hardware inventory'
                ? sourceLinks?.inventory
                : source.source === 'Security posture'
                  ? sourceLinks?.security
                  : undefined;
              const sourceLinkLabel = source.source === 'Hardware inventory'
                ? 'Open Inventory'
                : source.source === 'Security posture'
                  ? 'Open Security'
                  : null;
              return (
                <li key={source.source} className="rounded border border-slate-800 bg-slate-950/40 p-3">
                  <div className="flex items-center justify-between gap-3">
                    <span className="font-medium text-slate-200">{source.source}</span>
                    <SemanticStatusBadge status={reportSourceStatus(source.state)} />
                  </div>
                  <p className="mt-1 text-sm text-slate-300">{source.summary}</p>
                  <dl className="mt-2 grid grid-cols-[auto_1fr] gap-x-3 text-xs text-slate-400">
                    <dt>Age</dt>
                    <dd>{formatDataAge(source.ageSeconds)}</dd>
                    <dt>Captured</dt>
                    <dd>{source.capturedAtUtc ? new Date(source.capturedAtUtc).toLocaleString() : 'Unavailable'}</dd>
                    <dt>Provenance</dt>
                    <dd>{source.provenance}</dd>
                  </dl>
                  {source.state !== 'READY' && sourceLink && sourceLinkLabel && (
                    <Link
                      to={sourceLink}
                      className="mt-3 inline-flex items-center gap-1 text-sm font-medium text-accent-400 hover:text-accent-300"
                    >
                      {sourceLinkLabel} <span aria-hidden="true">→</span>
                    </Link>
                  )}
                </li>
              );
            })}
          </ul>
        </div>
      </Card>

      <Card title={`Included data — ${subject}`}>
        <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-1 text-sm">
          <dt className="text-slate-400">Hardware inventory</dt>
          <dd>
            {overview.inventoryCapturedAtUtc
              ? `Snapshot from ${new Date(overview.inventoryCapturedAtUtc).toLocaleString()}`
              : 'No snapshot yet — run the Inventory section for this machine first.'}
          </dd>
          <dt className="text-slate-400">Security scan</dt>
          <dd>
            {overview.securityScanCompletedAtUtc
              ? `${new Date(overview.securityScanCompletedAtUtc).toLocaleString()} — ${
                  overview.securityScanStatus ?? ''
                }, ${overview.securityFindingCount ?? 0} findings`
              : 'No scan yet — run the Security section for this machine first.'}
          </dd>
          {overview.securityScanCompletedAtUtc && (
            <>
              <dt className="text-slate-400">Security coverage</dt>
              <dd
                className={
                  overview.securityCoverage?.isComplete ? 'text-ok-400' : 'text-warn-400'
                }
              >
                {overview.securityCoverage?.isComplete
                  ? `${overview.securityCoverage.succeededChecks}/${overview.securityCoverage.applicableChecks} applicable checks evaluated`
                  : overview.securityCoverage?.isKnown
                    ? `${overview.securityCoverage.succeededChecks}/${overview.securityCoverage.applicableChecks} applicable checks evaluated — incomplete`
                    : 'Unavailable for this legacy scan — findings are not a complete assessment.'}
              </dd>
            </>
          )}
          <dt className="text-slate-400">Diagnostics</dt>
          <dd className="text-slate-400">
            Not included — diagnostics are outside the report read contract. Export does not run checks or include the saved latest diagnostics run.
          </dd>
        </dl>
        <p className="mt-3 text-xs text-muted">
          The report reflects the data already captured for {subject}; it never starts a scan.
        </p>
      </Card>

      <Card title="Export">
        <div className="flex flex-col gap-3">
          <Checkbox
            label="Open the report after export"
            checked={openAfterExport}
            onChange={(event) => setOpenAfterExport(event.target.checked)}
          />
          <div className="flex items-center gap-2">
            <Button
              variant="primary"
              onClick={() => exportReport('exportHtml')}
              disabled={!hasAnyData || exportState.kind === 'exporting'}
            >
              Export HTML
            </Button>
            <Button
              onClick={() => exportReport('exportJson')}
              disabled={!hasAnyData || exportState.kind === 'exporting'}
            >
              Export JSON
            </Button>
            {exportState.kind === 'exporting' && (
              <span className="text-sm text-slate-400">Waiting for save dialog …</span>
            )}
          </div>

          {!hasAnyData && (
            <p className="text-sm text-slate-400">
              Nothing to export yet — capture inventory data or run a security scan for {subject} first.
            </p>
          )}
          {hasAnyData && !overview.readiness.isReady && (
            <p className="text-sm text-warn-400">
              Export remains available, but the report will record that its source data is not ready.
            </p>
          )}
          {exportState.kind === 'exported' && (
            <div>
              <p className="text-sm text-ok-400">
                Report written to <span className="font-mono text-ok-300">{exportState.filePath}</span>
              </p>
              {exportState.openError && (
                <div className="mt-3">
                  <ErrorState title="Report saved, but opening failed" {...exportState.openError} />
                </div>
              )}
            </div>
          )}
          {exportState.kind === 'cancelled' && <p className="text-sm text-slate-400">Export cancelled.</p>}
          {exportState.kind === 'error' && (
            <ErrorState title="Report export failed" {...exportState.error} />
          )}
        </div>
      </Card>
    </div>
  );
}
