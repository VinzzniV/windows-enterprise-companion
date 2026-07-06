import type { ReactNode } from 'react';
import { Card } from './Card';

interface EmptyStateProps {
  title: string;
  message: string;
  /** Optional action (button) that resolves the empty state. */
  action?: ReactNode;
}

/** "Nothing here yet" with the way forward — never a blank pane. */
export function EmptyState({ title, message, action }: EmptyStateProps) {
  return (
    <Card title={title}>
      <div className="flex flex-col gap-3">
        <p className="text-sm text-slate-400">{message}</p>
        {action && <div>{action}</div>}
      </div>
    </Card>
  );
}

interface ErrorStateProps {
  title?: string;
  message: string;
  /** Optional action-oriented hint below the raw message. */
  hint?: string;
}

/** Failure card: what failed, and when known, what to do about it. */
export function ErrorState({ title = 'Error', message, hint }: ErrorStateProps) {
  return (
    <Card title={title}>
      <div role="alert" className="flex flex-col gap-2">
        <p className="break-words text-sm text-fail-400">{message}</p>
        {hint && <p className="text-sm text-slate-400">{hint}</p>}
      </div>
    </Card>
  );
}
