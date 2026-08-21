import type { ReactNode } from 'react';

export type MetricTone = 'neutral' | 'success' | 'warning' | 'danger' | 'info';

// Fixed semantics: success = healthy/nothing-wrong, warning = attention,
// danger = broken/failing, info = notable count, neutral = plain total.
const toneStyles: Record<MetricTone, string> = {
  neutral: 'text-slate-100',
  success: 'text-ok-400',
  warning: 'text-warn-400',
  danger: 'text-fail-400',
  info: 'text-info-400',
};

interface SummaryMetricProps {
  label: string;
  value: ReactNode;
  tone?: MetricTone;
  onClick?: () => void;
  active?: boolean;
  ariaLabel?: string;
}

/** One number-over-label tile for the summary strip on top of result views. */
export function SummaryMetric({ label, value, tone = 'neutral', onClick, active = false, ariaLabel }: SummaryMetricProps) {
  const content = <>
      <div className={`text-xl font-semibold tabular-nums ${toneStyles[tone]}`}>{value}</div>
      <div className="text-xs text-slate-400">{label}</div>
    </>;
  const baseClass = 'min-w-24 rounded border bg-slate-900/50 px-3 py-2';

  if (onClick) {
    return (
      <button
        type="button"
        onClick={onClick}
        aria-label={ariaLabel}
        aria-pressed={active}
        className={`${baseClass} cursor-pointer text-left transition-colors focus:outline-none focus:ring-2 focus:ring-accent-500 ${active ? 'border-accent-500 bg-accent-500/10' : 'border-slate-800 hover:border-slate-600 hover:bg-slate-800/60'}`}
      >
        {content}
      </button>
    );
  }

  return <div className={`${baseClass} border-slate-800`}>{content}</div>;
}
