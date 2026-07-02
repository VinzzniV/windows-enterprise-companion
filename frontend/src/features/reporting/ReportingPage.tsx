import { useCallback, useEffect, useState } from 'react';
import { invoke } from '../../shared/bridge/bridgeClient';
import type { ExportReportRequest, ReportExportResult, ReportOverview } from '../../shared/api-types';
import { Card } from '../../shared/ui/Card';

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
      <header>
        <h1 className="text-xl font-semibold">Reporting</h1>
        <p className="text-sm text-slate-400">Executive summary export of the local machine</p>
      </header>

      {overviewState.kind === 'loading' && <p className="text-sm text-slate-400">Loading overview …</p>}

      {overviewState.kind === 'error' && (
        <Card title="Error">
          <p className="text-sm text-red-400">{overviewState.message}</p>
        </Card>
      )}

      {overview && (
        <Card title="Included data">
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
        </Card>
      )}

      {overview && (
        <Card title="Export">
          <div className="flex flex-col gap-3">
            <label className="flex items-center gap-2 text-sm">
              <input
                type="checkbox"
                checked={openAfterExport}
                onChange={(event) => setOpenAfterExport(event.target.checked)}
                className="accent-slate-400"
              />
              Open the report after export
            </label>
            <div className="flex items-center gap-2">
              <button
                type="button"
                onClick={() => exportReport('exportHtml')}
                disabled={!hasAnyData || exportState.kind === 'exporting'}
                className="rounded bg-slate-700 px-3 py-1.5 text-sm font-medium text-slate-100 transition-colors hover:bg-slate-600 disabled:opacity-50"
              >
                Export HTML
              </button>
              <button
                type="button"
                onClick={() => exportReport('exportJson')}
                disabled={!hasAnyData || exportState.kind === 'exporting'}
                className="rounded border border-slate-600 px-3 py-1.5 text-sm font-medium text-slate-200 transition-colors hover:bg-slate-800 disabled:opacity-50"
              >
                Export JSON
              </button>
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
              <p className="text-sm text-emerald-400">
                Report written to <span className="font-mono text-emerald-300">{exportState.filePath}</span>
              </p>
            )}
            {exportState.kind === 'cancelled' && (
              <p className="text-sm text-slate-400">Export cancelled.</p>
            )}
            {exportState.kind === 'error' && (
              <p className="text-sm text-red-400">{exportState.message}</p>
            )}
          </div>
        </Card>
      )}
    </div>
  );
}
