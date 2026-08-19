import type { ReactNode } from 'react';
import { Badge, type BadgeTone } from './Badge';

export type StatusBadgeVariant = 'success' | 'error' | 'warning' | 'elevation' | 'neutral' | 'info';

const variantTone: Record<StatusBadgeVariant, BadgeTone> = {
  success: 'ok',
  error: 'fail',
  warning: 'warn',
  elevation: 'warn',
  neutral: 'neutral',
  info: 'info',
};

interface StatusBadgeProps {
  variant: StatusBadgeVariant;
  children: ReactNode;
}

export function StatusBadge({ variant, children }: StatusBadgeProps) {
  return <Badge tone={variantTone[variant]}>{children}</Badge>;
}
