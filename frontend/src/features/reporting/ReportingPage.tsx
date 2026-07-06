import { useCallback, useEffect, useState } from 'react';
import { invoke } from '../../shared/bridge/bridgeClient';
import type { ExportReportRequest, ReportExportResult, ReportOverview } from '../../shared/api-types';
import { Card } from '../../shared/ui/Card';
import { Spinner } from '../../shared/ui/Spinner';
import { Button } from '../../shared/ui/Button';
import { PageHeader } from '../../shared/ui/PageHeader';
import { ErrorState } from '../../shared/ui/States';
import { StatusBadge } from '../../shared/ui/StatusBadge';
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

export function ReportingPage() {
  const [overviewState, setOverviewState] = useState<OverviewState>({ kind: 'loading' });
  const [exportState, setExportState] = useState<ExportState>({ kind: 'idle' });
  const [openAfterExport, setOpenAfterExport] = useState(true);

  useEffect(() => {
    invoke<ReportOverview>('reporting', 'getOverview')
      .then((overview) => setOverviewState({ kind: 'loaded', overview }))
      .catch((error: unknown) =>
        setOverviewState({
          kind: 'error',
          message: error instanceof Error ? error.message : String(error),
        }),
      );
  }, []);

  const exportReport = useCallback(
    (action: 'exportHtml' | 'exportJson') => {
      setExportState({ kind: 'exporting' });
      const payload: ExportReportRequest = { openAfterExport };
      invoke<ReportExportResult>('reporting', action, payload, 120_000)
        .then((result) =>
          setExportState(
            result.cancelled || result.filePath === null
              ? { kind: 'cancelled' }
              : { kind: 'exported', filePath: result.filePath },
          ),
        )
        .catch((error: unknown) =>
          setExportState({
            kind: 'error',
            message: error instanceof Error ? error.message : String(error),
          }),
        );
    },
    [openAfterExport],
  );

  const overview = overviewState.kind === 'loaded' ? overviewState.overview : null;
  const hasAnyData =
    overview !== null &&
    (overview.inventoryCapturedAtUtc !== null || overview.securityScanCompletedAtUtc !== null);

  return (
    <div className="flex flex-col gap-4">
      <PageHeader title="Reporting" subtitle="Executive summary export">
        <StatusBadge variant="neutral">Local machine only</StatusBadge>
      </PageHeader>

      {overviewState.kind === 'loading' && <Spinner label="Loading overview …" />}

      {overviewState.kind === 'error' && <ErrorState message={overviewState.message} />}

      {overview && (
        <Card title="Included data — this machine">
          <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-1 text-sm">
            <dt className="text-slate-400">Hardware inventory</dt>
            <dd>
              {overview.inventoryCapturedAtUtc
                ? `Snapshot from ${new Date(overview.inventoryCapturedAtUtc).toLocaleString()}`
                : 'No snapshot yet — open the Inventory page first.'}
            </dd>
            <dt className="text-slate-400">Security scan</dt>
            <dd>
              {overview.securityScanCompletedAtUtc
                ? `${new Date(overview.securityScanCompletedAtUtc).toLocaleString()} — ${
                    overview.securityScanStatus ?? ''
                  }, ${overview.securityFindingCount ?? 0} findings`
                : 'No scan yet — run one on the Security page first.'}
            </dd>
            <dt className="text-slate-400">Diagnostics</dt>
            <dd className="text-slate-400">
              Not included — diagnostics are live-only and never triggered silently by an export.
            </dd>
          </dl>
          <p className="mt-3 text-xs text-slate-500">
            Reports cover the machine WEC runs on. Remote scan results are not included yet
            (recorded follow-up in TODO).
          </p>
        </Card>
      )}

      {overview && (
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
                Nothing to export yet — capture inventory data or run a security scan first.
              </p>
            )}
            {exportState.kind === 'exported' && (
              <p className="text-sm text-ok-400">
                Report written to <span className="font-mono text-ok-300">{exportState.filePath}</span>
              </p>
            )}
            {exportState.kind === 'cancelled' && (
              <p className="text-sm text-slate-400">Export cancelled.</p>
            )}
            {exportState.kind === 'error' && (
              <p className="text-sm text-fail-400">{exportState.message}</p>
            )}
          </div>
        </Card>
      )}
    </div>
  );
}
