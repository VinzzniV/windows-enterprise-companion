import { useCallback, useEffect, useMemo, useState } from 'react';
import { invoke } from '../../shared/bridge/bridgeClient';
import type {
  FindingCategory,
  FindingSeverity,
  LatestScanResult,
  SecurityFinding,
  SecurityScanResult,
} from '../../shared/api-types';
import { Card } from '../../shared/ui/Card';
import { StatusBadge } from '../../shared/ui/StatusBadge';
import { Spinner } from '../../shared/ui/Spinner';
import { SeverityBadge } from './SeverityBadge';
import { ScanHistory } from './ScanHistory';
import {
  LOCAL_TARGET_SELECTION,
  TargetSelector,
  toTargetRequest,
  type TargetSelection,
} from '../../shared/targets/TargetSelector';

const allSeverities: FindingSeverity[] = ['CRITICAL', 'HIGH', 'MEDIUM', 'LOW', 'INFO'];

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

function FindingCard({ finding }: { finding: SecurityFinding }) {
  return (
    <li className="rounded-lg border border-slate-800 bg-slate-900 p-4">
      <div className="flex items-start justify-between gap-3">
        <div>
          <h3 className="text-sm font-semibold">{finding.title}</h3>
          <p className="text-xs text-slate-500">{finding.affectedResource}</p>
        </div>
        <div className="flex shrink-0 items-center gap-2">
          {finding.requiredPrivilege && (
            <StatusBadge variant="elevation">Requires elevation</StatusBadge>
          )}
          <SeverityBadge severity={finding.severity} />
        </div>
      </div>
      <p className="mt-2 text-sm text-slate-300">{finding.description}</p>
      <dl className="mt-3 grid grid-cols-[auto_1fr] gap-x-4 gap-y-0.5 rounded bg-slate-950/60 p-2 text-xs">
        {Object.entries(finding.evidence).map(([key, value]) => (
          <div key={key} className="contents">
            <dt className="text-slate-500">{key}</dt>
            <dd className="break-all text-slate-300">{value}</dd>
          </div>
        ))}
      </dl>
      <p className="mt-3 text-sm text-slate-400">
        <span className="font-medium text-slate-300">Recommendation: </span>
        {finding.recommendation}
      </p>
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

  useEffect(() => {
    setState({ kind: 'loading' });
    invoke<LatestScanResult>('security', 'getLatestScan', { target: activeTarget })
      .then((result) => setState({ kind: 'loaded', scan: result.scan }))
      .catch((error: unknown) =>
        setState({ kind: 'error', message: error instanceof Error ? error.message : String(error) }),
      );
  }, [activeTarget]);

  const runScan = useCallback(() => {
    const target = toTargetRequest(selection);
    setScanning(true);
    invoke<SecurityScanResult>('security', 'runScan', { target }, 120_000)
      .then((scan) => {
        setActiveTarget(target);
        setState({ kind: 'loaded', scan });
      })
      .catch((error: unknown) =>
        setState({ kind: 'error', message: error instanceof Error ? error.message : String(error) }),
      )
      .finally(() => setScanning(false));
  }, [selection]);

  const scan = state.kind === 'loaded' ? state.scan : null;

  const availableCategories = useMemo(
    () => [...new Set(scan?.findings.map((finding) => finding.category) ?? [])],
    [scan],
  );

  const visibleFindings = useMemo(
    () =>
      (scan?.findings ?? []).filter(
        (finding) =>
          !hiddenSeverities.has(finding.severity) &&
          (categoryFilter === 'ALL' || finding.category === categoryFilter),
      ),
    [scan, hiddenSeverities, categoryFilter],
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
      <header className="flex flex-col gap-3">
        <div className="flex items-end justify-between">
          <div>
            <h1 className="text-xl font-semibold">Security</h1>
            <p className="text-sm text-slate-400">Read-only security findings per scanned computer</p>
          </div>
          <div className="flex items-center gap-3">
            {scan && (
              <span className="flex items-center gap-2 text-xs text-slate-400">
                <span className="font-medium text-slate-300">{scan.host}</span>
                <StatusBadge
                  variant={
                    scan.status === 'COMPLETED'
                      ? 'success'
                      : scan.status === 'COMPLETED_WITH_ERRORS'
                        ? 'elevation'
                        : 'error'
                  }
                >
                  {scan.status.replaceAll('_', ' ')}
                </StatusBadge>
                {new Date(scan.completedAtUtc).toLocaleString()} — {scan.findings.length} findings
              </span>
            )}
            <button
              type="button"
              onClick={runScan}
              disabled={scanning || state.kind === 'loading' || (selection.mode === 'remote' && selection.host.trim() === '')}
              className="rounded bg-slate-700 px-3 py-1.5 text-sm font-medium text-slate-100 transition-colors hover:bg-slate-600 disabled:opacity-50"
            >
              {scanning ? 'Scanning …' : 'Run scan'}
            </button>
          </div>
        </div>
        <TargetSelector selection={selection} onChange={setSelection} disabled={scanning} />
      </header>

      {state.kind === 'loading' && <Spinner label="Loading latest scan …" />}

      {state.kind === 'error' && (
        <Card title="Error">
          <p className="text-sm text-red-400">{state.message}</p>
        </Card>
      )}

      {state.kind === 'loaded' && !scan && (
        <Card title="No scan yet">
          <p className="text-sm text-slate-400">
            No security scan has been run against this target. Start one with "Run scan".
          </p>
        </Card>
      )}

      {scan && (
        <>
          <div className="flex flex-wrap items-center gap-2 text-xs">
            <span className="text-slate-500">Severity:</span>
            {allSeverities.map((severity) => (
              <button
                key={severity}
                type="button"
                onClick={() => toggleSeverity(severity)}
                className={`rounded border px-2 py-0.5 transition-colors ${
                  hiddenSeverities.has(severity)
                    ? 'border-slate-800 text-slate-600'
                    : 'border-slate-600 text-slate-200'
                }`}
              >
                {severity}
              </button>
            ))}
            <span className="ml-4 text-slate-500">Category:</span>
            <select
              value={categoryFilter}
              onChange={(event) => setCategoryFilter(event.target.value as FindingCategory | 'ALL')}
              className="rounded border border-slate-700 bg-slate-900 px-2 py-0.5 text-slate-200"
            >
              <option value="ALL">All</option>
              {availableCategories.map((category) => (
                <option key={category} value={category}>
                  {categoryLabel(category)}
                </option>
              ))}
            </select>
          </div>

          {scan.findings.length === 0 ? (
            <Card title="Result">
              <p className="text-sm text-emerald-400">
                No findings — all executed checks passed.
              </p>
            </Card>
          ) : (
            <ul className="flex flex-col gap-3">
              {visibleFindings.map((finding, index) => (
                <FindingCard key={`${finding.findingId}-${index}`} finding={finding} />
              ))}
              {visibleFindings.length === 0 && (
                <li className="text-sm text-slate-400">All findings are hidden by the current filters.</li>
              )}
            </ul>
          )}
        </>
      )}

      {state.kind === 'loaded' && (
        <ScanHistory refreshToken={scan?.scanId ?? null} target={activeTarget} />
      )}
    </div>
  );
}
