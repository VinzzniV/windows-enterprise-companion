import type { DiagnosticCategory, DiagnosticResult, DiagnosticStatus } from '../../shared/api-types';
import { DetailsDisclosure } from '../../shared/ui/DetailsDisclosure';
import { EvidenceList } from '../../shared/ui/EvidenceList';
import { SemanticStatusBadge, type SemanticStatus } from '../../shared/ui/SemanticStatusBadge';
import { StatusBadge } from '../../shared/ui/StatusBadge';
import { SummaryMetric } from '../../shared/ui/SummaryMetric';

const categoryOrder: DiagnosticCategory[] = [
  'SERVICES',
  'EVENT_LOG',
  'SYSTEM',
];

const categoryLabels: Record<DiagnosticCategory, string> = {
  SERVICES: 'Services',
  EVENT_LOG: 'Event logs',
  SYSTEM: 'System',
};

const statusSemantics: Record<DiagnosticStatus, SemanticStatus> = {
  PASS: { dimension: 'health', value: 'healthy' },
  WARNING: { dimension: 'health', value: 'warning' },
  FAIL: { dimension: 'health', value: 'critical' },
  NOT_RUN: { dimension: 'availability', value: 'unknown' },
};

const statusOrder: Record<DiagnosticStatus, number> = {
  FAIL: 0,
  WARNING: 1,
  NOT_RUN: 2,
  PASS: 3,
};

function DiagnosticStatusBadge({ status }: { status: DiagnosticStatus }) {
  return <SemanticStatusBadge status={statusSemantics[status]} />;
}

function countByStatus(results: DiagnosticResult[], status: DiagnosticStatus): number {
  return results.filter((result) => result.status === status).length;
}

function DiagnosticRow({ result }: { result: DiagnosticResult }) {
  const evidenceCount = Object.keys(result.evidence).length;

  return (
    <li className="rounded-lg border border-slate-800 bg-slate-900 p-3">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <h3 className="text-sm font-semibold">{result.title}</h3>
          <p className="break-words text-xs text-muted">{result.affectedResource}</p>
        </div>
        <div className="flex shrink-0 items-center gap-2">
          {result.requiredPrivilege && <StatusBadge variant="elevation">Requires elevation</StatusBadge>}
          <DiagnosticStatusBadge status={result.status} />
        </div>
      </div>

      {result.suggestedNextSteps.length > 0 && (
        <ul className="mt-2 list-disc space-y-0.5 pl-5 text-sm text-slate-300">
          {result.suggestedNextSteps.map((step, index) => (
            <li key={index}>{step}</li>
          ))}
        </ul>
      )}

      {evidenceCount > 0 && (
        <div className="mt-2">
          <DetailsDisclosure summary={`Raw evidence (${evidenceCount})`}>
            <EvidenceList evidence={result.evidence} />
          </DetailsDisclosure>
        </div>
      )}
    </li>
  );
}

export function RunSummary({ results }: { results: DiagnosticResult[] }) {
  const warningCount = countByStatus(results, 'WARNING');
  const failCount = countByStatus(results, 'FAIL');
  return (
    <div className="flex flex-wrap gap-2">
      <SummaryMetric label="Critical" value={failCount} tone={failCount > 0 ? 'danger' : 'neutral'} />
      <SummaryMetric label="Warning" value={warningCount} tone={warningCount > 0 ? 'warning' : 'neutral'} />
      <SummaryMetric label="Unknown" value={countByStatus(results, 'NOT_RUN')} tone="neutral" />
      <SummaryMetric label="Healthy" value={countByStatus(results, 'PASS')} tone="success" />
    </div>
  );
}

export function CategorySections({ results }: { results: DiagnosticResult[] }) {
  return (
    <>
      {categoryOrder
        .map((category) => ({
          category,
          results: results
            .filter((result) => result.category === category)
            .sort((left, right) => statusOrder[left.status] - statusOrder[right.status]),
        }))
        .filter((group) => group.results.length > 0)
        .sort((left, right) => {
          const statusDifference =
            statusOrder[left.results[0].status] - statusOrder[right.results[0].status];
          return statusDifference !== 0
            ? statusDifference
            : categoryOrder.indexOf(left.category) - categoryOrder.indexOf(right.category);
        })
        .map((group) => {
          const attention = group.results.filter((result) => result.status !== 'PASS').length;
          return (
            <section key={group.category} aria-label={categoryLabels[group.category]}>
              <h2 className="mb-2 flex items-baseline gap-2 border-b border-slate-800 pb-1 text-sm font-medium uppercase tracking-wide text-slate-400">
                {categoryLabels[group.category]}
                <span className="text-xs font-normal normal-case tracking-normal text-muted">
                  {group.results.length} check{group.results.length === 1 ? '' : 's'}
                  {attention > 0 && ` · ${attention} need${attention === 1 ? 's' : ''} attention`}
                </span>
              </h2>
              <ul className="flex flex-col gap-2">
                {group.results.map((result, index) => (
                  <DiagnosticRow key={`${result.diagnosticId}-${index}`} result={result} />
                ))}
              </ul>
            </section>
          );
        })}
    </>
  );
}
