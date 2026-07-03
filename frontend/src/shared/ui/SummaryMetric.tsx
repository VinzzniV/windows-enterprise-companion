import type { ReactNode } from 'react';

export type MetricTone = 'neutral' | 'success' | 'warning' | 'danger' | 'info';

const toneStyles: Record<MetricTone, string> = {
  neutral: 'text-slate-100',
  success: 'text-emerald-400',
  warning: 'text-amber-400',
  danger: 'text-red-400',
  info: 'text-sky-400',
};

interface SummaryMetricProps {
  label: string;
  value: ReactNode;
  tone?: MetricTone;
}

/** One number-over-label tile for the summary strip on top of result views. */
export function SummaryMetric({ label, value, tone = 'neutral' }: SummaryMetricProps) {
  return (
    <div className="min-w-24 rounded border border-slate-800 bg-slate-900/50 px-3 py-2">
      <div className={`text-xl font-semibold tabular-nums ${toneStyles[tone]}`}>{value}</div>
      <div className="text-xs text-slate-400">{label}</div>
    </div>
  );
}
