import type { ReactNode } from 'react';

export type StatusBadgeVariant = 'success' | 'error' | 'elevation' | 'neutral';

const variantStyles: Record<StatusBadgeVariant, string> = {
  success: 'border-emerald-700 bg-emerald-900/60 text-emerald-300',
  error: 'border-red-700 bg-red-900/60 text-red-300',
  elevation: 'border-amber-700 bg-amber-900/60 text-amber-300',
  neutral: 'border-slate-700 bg-slate-800 text-slate-300',
};

interface StatusBadgeProps {
  variant: StatusBadgeVariant;
  children: ReactNode;
}

export function StatusBadge({ variant, children }: StatusBadgeProps) {
  return (
    <span
      className={`inline-flex items-center gap-1 rounded border px-2 py-0.5 text-xs font-medium ${variantStyles[variant]}`}
    >
      {children}
    </span>
  );
}
