import type { FindingSeverity } from '../../shared/api-types';

const severityStyles: Record<FindingSeverity, string> = {
  CRITICAL: 'border-red-600 bg-red-950 text-red-300',
  HIGH: 'border-red-700 bg-red-900/60 text-red-300',
  MEDIUM: 'border-amber-700 bg-amber-900/60 text-amber-300',
  LOW: 'border-sky-700 bg-sky-900/60 text-sky-300',
  INFO: 'border-slate-700 bg-slate-800 text-slate-300',
};

export function SeverityBadge({ severity, count }: { severity: FindingSeverity; count?: number }) {
  return (
    <span
      className={`inline-flex items-center gap-1 rounded border px-2 py-0.5 text-xs font-medium ${severityStyles[severity]}`}
    >
      {severity}
      {count !== undefined && <span className="font-normal opacity-80">{count}</span>}
    </span>
  );
}
