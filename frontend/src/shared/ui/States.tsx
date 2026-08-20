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
  cause?: string;
  action?: string;
  technicalDetails?: string;
  controls?: ReactNode;
}

interface CompactErrorStateProps {
  title?: string;
  message: string;
  hint?: string;
  cause?: string;
  action?: string;
  technicalDetails?: string;
  className?: string;
}

/** Dense failure presentation for constrained shell surfaces. */
export function CompactErrorState({
  title,
  message,
  hint,
  cause,
  action,
  technicalDetails,
  className = '',
}: CompactErrorStateProps) {
  return (
    <div
      role="alert"
      className={`min-w-0 rounded border border-fail-800/70 bg-fail-950/30 px-3 py-2 text-xs ${className}`}
    >
      {title && <p className="font-semibold text-fail-200">{title}</p>}
      <p className={`${title ? 'mt-1 ' : ''}break-words font-medium text-fail-300`}>{message}</p>
      {cause && (
        <div className="mt-2">
          <span className="font-medium uppercase tracking-wide text-muted">Cause</span>
          <p className="mt-0.5 break-words text-slate-300">{cause}</p>
        </div>
      )}
      {(action || hint) && (
        <div className="mt-2">
          <span className="font-medium uppercase tracking-wide text-muted">Next action</span>
          <p className="mt-0.5 break-words text-slate-300">{action ?? hint}</p>
        </div>
      )}
      {technicalDetails && (
        <details className="mt-2 border-t border-slate-800 pt-2">
          <summary className="cursor-pointer font-medium text-slate-400 hover:text-slate-200">
            Technical details
          </summary>
          <pre className="mt-2 max-h-32 overflow-auto whitespace-pre-wrap break-words font-mono text-[11px] text-slate-400">
            {technicalDetails}
          </pre>
        </details>
      )}
    </div>
  );
}

/** Failure card: what failed, and when known, what to do about it. */
export function ErrorState({
  title = 'Error',
  message,
  hint,
  cause,
  action,
  technicalDetails,
  controls,
}: ErrorStateProps) {
  return (
    <Card title={title}>
      <div role="alert" className="flex flex-col gap-3">
        <p className="break-words text-sm font-medium text-fail-300">{message}</p>
        {cause && (
          <div className="flex flex-col gap-0.5">
            <span className="text-xs font-medium uppercase tracking-wide text-muted">Cause</span>
            <p className="break-words text-sm text-slate-300">{cause}</p>
          </div>
        )}
        {(action || hint) && (
          <div className="flex flex-col gap-0.5">
            <span className="text-xs font-medium uppercase tracking-wide text-muted">Next action</span>
            <p className="break-words text-sm text-slate-300">{action ?? hint}</p>
          </div>
        )}
        {technicalDetails && (
          <details className="rounded border border-slate-800 bg-slate-950/50 px-3 py-2">
            <summary className="cursor-pointer text-xs font-medium text-slate-400 hover:text-slate-200">
              Technical details
            </summary>
            <pre className="mt-2 whitespace-pre-wrap break-words font-mono text-xs text-slate-400">
              {technicalDetails}
            </pre>
          </details>
        )}
        {controls && <div className="flex flex-wrap gap-2">{controls}</div>}
      </div>
    </Card>
  );
}
