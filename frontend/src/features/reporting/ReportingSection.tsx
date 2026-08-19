import { useCallback, useEffect, useState } from 'react';
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

type OverviewState =
  | { kind: 'loading' }
  | { kind: 'loaded'; overview: ReportOverview }
  | { kind: 'error'; message: string };

type ExportState =
  | { kind: 'idle' }
  | { kind: 'exporting' }
  | { kind: 'exported'; filePath: string }
  | { kind: 'cancelled' }
  | { kind: 'error'; message: string };

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

/**
 * Executive-summary overview + export for one machine. `host === null` targets
 * the local machine; a host string targets a scanned remote client. The report
 * reads whatever inventory/security data was already captured for that host —
 * it never triggers a live scan.
 */
export function ReportingSection({ host }: { host: string | null }) {
  const [overviewState, setOverviewState] = useState<OverviewState>({ kind: 'loading' });
  const [exportState, setExportState] = useState<ExportState>({ kind: 'idle' });
  const [openAfterExport, setOpenAfterExport] = useState(true);

  const subject = host === null ? 'this machine' : host;

  useEffect(() => {
    setOverviewState({ kind: 'loading' });
    const payload: ReportOverviewRequest = { host };
    invoke<ReportOverview>('reporting', 'getOverview', payload)
      .then((overview) => setOverviewState({ kind: 'loaded', overview }))
      .catch((error: unknown) => setOverviewState({ kind: 'error', message: errorMessage(error) }));
  }, [host]);

  const exportReport = useCallback(
    (action: 'exportHtml' | 'exportJson') => {
      setExportState({ kind: 'exporting' });
      const payload: ExportReportRequest = { openAfterExport, host };
      invoke<ReportExportResult>('reporting', action, payload, 120_000)
        .then((result) =>
          setExportState(
            result.cancelled || result.filePath === null
              ? { kind: 'cancelled' }
              : { kind: 'exported', filePath: result.filePath },
          ),
        )
        .catch((error: unknown) => setExportState({ kind: 'error', message: errorMessage(error) }));
    },
    [openAfterExport, host],
  );

  if (overviewState.kind === 'loading') {
    return <Spinner label="Loading overview …" />;
  }
  if (overviewState.kind === 'error') {
    return <ErrorState message={overviewState.message} />;
  }

  const { overview } = overviewState;
  const hasAnyData =
    overview.inventoryCapturedAtUtc !== null || overview.securityScanCompletedAtUtc !== null;

  return (
    <div className="flex flex-col gap-4">
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
            Not included — diagnostics are live-only and never triggered silently by an export.
          </dd>
        </dl>
        <p className="mt-3 text-xs text-slate-500">
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
          {exportState.kind === 'exported' && (
            <p className="text-sm text-ok-400">
              Report written to <span className="font-mono text-ok-300">{exportState.filePath}</span>
            </p>
          )}
          {exportState.kind === 'cancelled' && <p className="text-sm text-slate-400">Export cancelled.</p>}
          {exportState.kind === 'error' && <p className="text-sm text-fail-400">{exportState.message}</p>}
        </div>
      </Card>
    </div>
  );
}
