import type { ReactNode } from 'react';

/** Semantic status tone shared by every badge (color is never the only signal — a label always accompanies it). */
export type BadgeTone = 'ok' | 'warn' | 'fail' | 'info' | 'neutral' | 'accent';

const toneStyles: Record<BadgeTone, string> = {
  ok: 'border-ok-700 bg-ok-900/50 text-ok-300',
  warn: 'border-warn-700 bg-warn-900/50 text-warn-300',
  fail: 'border-fail-700 bg-fail-900/50 text-fail-300',
  info: 'border-info-700 bg-info-900/50 text-info-300',
  neutral: 'border-slate-700 bg-slate-800 text-slate-300',
  accent: 'border-accent-700 bg-accent-500/15 text-accent-300',
};

const dotStyles: Record<BadgeTone, string> = {
  ok: 'bg-ok-400',
  warn: 'bg-warn-400',
  fail: 'bg-fail-400',
  info: 'bg-info-400',
  neutral: 'bg-slate-400',
  accent: 'bg-accent-400',
};

interface BadgeProps {
  tone?: BadgeTone;
  /** Leading status dot; on by default. */
  dot?: boolean;
  children: ReactNode;
}

export function Badge({ tone = 'neutral', dot = true, children }: BadgeProps) {
  return (
    <span
      className={`inline-flex items-center gap-1.5 whitespace-nowrap break-normal rounded border px-2 py-0.5 text-xs font-medium ${toneStyles[tone]}`}
    >
      {dot && <span className={`h-1.5 w-1.5 shrink-0 rounded-full ${dotStyles[tone]}`} aria-hidden="true" />}
      {children}
    </span>
  );
}
