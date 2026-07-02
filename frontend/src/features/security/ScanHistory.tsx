import { useEffect, useState } from 'react';
import { invoke } from '../../shared/bridge/bridgeClient';
import type { ScanHistoryResult, ScanSummary } from '../../shared/api-types';
import { Card } from '../../shared/ui/Card';
import { Spinner } from '../../shared/ui/Spinner';
import { SeverityBadge } from './SeverityBadge';

type HistoryState =
  | { kind: 'loading' }
  | { kind: 'loaded'; history: ScanHistoryResult }
  | { kind: 'error'; message: string };

/** Inline sparkline of finding counts, oldest → newest. No chart dependency. */
function TrendSparkline({ scans }: { scans: ScanSummary[] }) {
  if (scans.length < 2) {
    return null;
  }

  const counts = [...scans].reverse().map((scan) => scan.findingCount);
  const max = Math.max(...counts, 1);
  const width = 160;
  const height = 36;
  const step = width / (counts.length - 1);
  const points = counts
    .map((count, index) => `${(index * step).toFixed(1)},${(height - 4 - (count / max) * (height - 8)).toFixed(1)}`)
    .join(' ');

  return (
    <svg
      viewBox={`0 0 ${width} ${height}`}
      className="h-9 w-40 shrink-0 text-sky-400"
      role="img"
      aria-label={`Finding count trend across ${counts.length} scans`}
    >
      <polyline points={points} fill="none" stroke="currentColor" strokeWidth="1.5" />
    </svg>
  );
}

interface ScanHistoryProps {
  /** Changes when a new scan lands; triggers a refetch. */
  refreshToken: number | null;
}

export function ScanHistory({ refreshToken }: ScanHistoryProps) {
  const [state, setState] = useState<HistoryState>({ kind: 'loading' });

  useEffect(() => {
    setState({ kind: 'loading' });
    invoke<ScanHistoryResult>('security', 'getScanHistory')
      .then((history) => setState({ kind: 'loaded', history }))
      .catch((error: unknown) =>
        setState({ kind: 'error', message: error instanceof Error ? error.message : String(error) }),
      );
  }, [refreshToken]);

  if (state.kind === 'loading') {
    return <Spinner label="Loading scan history …" />;
  }

  if (state.kind === 'error') {
    return (
      <Card title="Scan history">
        <p className="text-sm text-red-400">{state.message}</p>
      </Card>
    );
  }

  const { scans, changesSinceLastScan: changes } = state.history;
  if (scans.length === 0) {
    return null;
  }

  return (
    <>
      {changes && (
        <Card title="Changes since previous scan">
          {changes.newFindings.length === 0 && changes.resolvedFindings.length === 0 ? (
            <p className="text-sm text-slate-400">No changes — same findings as the previous scan.</p>
          ) : (
            <div className="flex flex-col gap-2 text-sm">
              {changes.newFindings.map((finding) => (
                <div key={`new-${finding.findingId}-${finding.affectedResource}`} className="flex items-center gap-2">
                  <span className="rounded border border-red-700 bg-red-900/60 px-1.5 text-xs text-red-300">new</span>
                  <SeverityBadge severity={finding.severity} />
                  <span>{finding.title}</span>
                </div>
              ))}
              {changes.resolvedFindings.map((finding) => (
                <div
                  key={`resolved-${finding.findingId}-${finding.affectedResource}`}
                  className="flex items-center gap-2"
                >
                  <span className="rounded border border-emerald-700 bg-emerald-900/60 px-1.5 text-xs text-emerald-300">
                    resolved
                  </span>
                  <span className="text-slate-400 line-through">{finding.title}</span>
                </div>
              ))}
            </div>
          )}
        </Card>
      )}

      <Card title={`Scan history (${scans.length})`}>
        <div className="flex flex-col gap-3">
          <TrendSparkline scans={scans} />
          <ul className="flex flex-col text-sm">
            {scans.map((scan) => (
              <li
                key={scan.scanId}
                className="flex flex-wrap items-center gap-x-3 gap-y-1 border-t border-slate-800 py-1.5 first:border-t-0"
              >
                <span className="w-40 text-slate-300">
                  {new Date(scan.completedAtUtc).toLocaleString()}
                </span>
                <span className="text-xs text-slate-500">{scan.status.replaceAll('_', ' ')}</span>
                <span className="text-xs text-slate-400">
                  {scan.findingCount} finding{scan.findingCount === 1 ? '' : 's'}
                </span>
                <span className="flex items-center gap-1">
                  {scan.severityCounts.map((entry) => (
                    <SeverityBadge key={entry.severity} severity={entry.severity} count={entry.count} />
                  ))}
                </span>
              </li>
            ))}
          </ul>
        </div>
      </Card>
    </>
  );
}
