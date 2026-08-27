import { useMemo, useState } from 'react';
import type {
  BatchScanResult,
  FindingCategory,
  FindingSeverity,
  HostScanStatus,
  SecurityCheckResult,
  SecurityCoverage,
  SecurityFinding,
  SecurityScanResult,
} from '../../shared/api-types';
import { Card } from '../../shared/ui/Card';
import { DetailsDisclosure } from '../../shared/ui/DetailsDisclosure';
import { EvidenceList } from '../../shared/ui/EvidenceList';
import { Select } from '../../shared/ui/Select';
import { SemanticStatusBadge, type SemanticStatus } from '../../shared/ui/SemanticStatusBadge';
import { StatusBadge, type StatusBadgeVariant } from '../../shared/ui/StatusBadge';
import { SummaryMetric } from '../../shared/ui/SummaryMetric';
import { SeverityBadge } from './SeverityBadge';

const hostStatusVariants: Record<HostScanStatus, StatusBadgeVariant> = {
  QUEUED: 'neutral',
  CONNECTING: 'info',
  RUNNING: 'info',
  COMPLETED: 'success',
  COMPLETED_WITH_ERRORS: 'elevation',
  FAILED: 'error',
};

export function HostStatusBadge({ status }: { status: HostScanStatus }) {
  return <StatusBadge variant={hostStatusVariants[status]}>{status.replaceAll('_', ' ')}</StatusBadge>;
}

const allSeverities: FindingSeverity[] = ['CRITICAL', 'HIGH', 'MEDIUM', 'LOW', 'INFO', 'UNKNOWN'];

function categoryLabel(category: FindingCategory): string {
  return category
    .toLowerCase()
    .split('_')
    .map((word) => word.charAt(0).toUpperCase() + word.slice(1))
    .join(' ');
}

function severityCount(findings: SecurityFinding[], severity: FindingSeverity): number {
  return findings.filter((finding) => finding.severity === severity).length;
}

export function FindingCard({ finding }: { finding: SecurityFinding }) {
  const evidenceCount = Object.keys(finding.evidence).length;
  return (
    <li className="rounded-lg border border-slate-800 bg-slate-900 p-4">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <h3 className="text-sm font-semibold">{finding.title}</h3>
          <p className="break-words text-xs text-muted">{finding.affectedResource}</p>
        </div>
        <div className="flex shrink-0 items-center gap-2">
          {finding.requiredPrivilege && (
            <StatusBadge variant="elevation">Requires elevation</StatusBadge>
          )}
          <SeverityBadge severity={finding.severity} />
        </div>
      </div>
      <p className="mt-2 text-sm text-slate-300">{finding.description}</p>
      <p className="mt-3 text-sm text-slate-400">
        <span className="font-medium text-slate-300">Recommendation: </span>
        {finding.recommendation}
      </p>
      {evidenceCount > 0 && (
        <div className="mt-3">
          <DetailsDisclosure summary={`Raw evidence (${evidenceCount})`}>
            <EvidenceList evidence={finding.evidence} />
          </DetailsDisclosure>
        </div>
      )}
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
            {result.failure && <span className="text-xs text-muted">{result.failure.message}</span>}
          </li>
        ))}
      </ul>
    </Card>
  );
}

export function securityCoverageSemanticStatus(coverage: SecurityCoverage): SemanticStatus {
  if (coverage.isComplete) {
    return { dimension: 'availability', value: 'available' };
  }
  return coverage.isKnown
    ? { dimension: 'execution', value: 'partial' }
    : { dimension: 'availability', value: 'unknown' };
}

function securityCoverageContext(coverage: SecurityCoverage): string {
  if (coverage.isComplete) {
    return 'Coverage complete';
  }
  return coverage.isKnown ? 'Coverage incomplete' : 'Coverage unavailable';
}

export function ResultContext({ scan, problemCount }: {
  scan: SecurityScanResult;
  problemCount: number;
}) {
  return (
    <div className="flex flex-wrap items-center gap-x-3 gap-y-1 rounded border border-slate-800 bg-slate-900/50 px-3 py-2 text-sm">
      <span className="font-medium">{scan.host}</span>
      <span className="inline-flex items-center gap-2">
        <SemanticStatusBadge status={securityCoverageSemanticStatus(scan.coverage)} />
        <span className="text-slate-400">{securityCoverageContext(scan.coverage)}</span>
      </span>
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

export function FindingList({ findings }: { findings: SecurityFinding[] }) {
  const [hiddenSeverities, setHiddenSeverities] = useState<Set<FindingSeverity>>(new Set());
  const [categoryFilter, setCategoryFilter] = useState<FindingCategory | 'ALL'>('ALL');
  const availableCategories = useMemo(
    () => [...new Set(findings.map((finding) => finding.category))],
    [findings],
  );
  const visibleFindings = useMemo(
    () =>
      findings.filter(
        (finding) =>
          !hiddenSeverities.has(finding.severity) &&
          (categoryFilter === 'ALL' || finding.category === categoryFilter),
      ),
    [findings, hiddenSeverities, categoryFilter],
  );

  const toggleSeverity = (severity: FindingSeverity) => {
    setHiddenSeverities((current) => {
      const next = new Set(current);
      if (next.has(severity)) next.delete(severity);
      else next.add(severity);
      return next;
    });
  };

  return (
    <div className="flex flex-col gap-3">
      <div
        className="flex flex-wrap items-center gap-2 text-xs"
        role="group"
        aria-label="Filter findings by severity"
      >
        <span className="text-slate-500" aria-hidden="true">
          Severity:
        </span>
        {allSeverities.map((severity) => {
          const count = severityCount(findings, severity);
          return (
            <button
              key={severity}
              type="button"
              onClick={() => toggleSeverity(severity)}
              aria-pressed={!hiddenSeverities.has(severity)}
              aria-label={`${severity}: ${count} ${count === 1 ? 'finding' : 'findings'}`}
              className={`cursor-pointer rounded border px-2 py-0.5 transition-colors ${
                hiddenSeverities.has(severity)
                  ? 'border-slate-800 text-slate-600'
                  : 'border-slate-600 text-slate-200'
              }`}
            >
              {severity} ({count})
            </button>
          );
        })}
        <span className="ml-4 text-slate-500" aria-hidden="true">
          Category:
        </span>
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
    </div>
  );
}

export function BatchHostRow({ outcome }: { outcome: BatchScanResult['hosts'][number] }) {
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
          <span className="text-xs text-muted">
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
