import { useId, type ReactNode } from 'react';

interface FieldProps {
  label: string;
  /** Render prop receives the id to wire onto the control for the label. */
  children: (controlId: string) => ReactNode;
  hint?: string;
  error?: string;
}

/** Label + control + optional hint/error, vertically stacked. */
export function Field({ label, children, hint, error }: FieldProps) {
  const controlId = useId();
  return (
    <div className="flex flex-col gap-1">
      <label htmlFor={controlId} className="text-xs font-medium text-slate-400">
        {label}
      </label>
      {children(controlId)}
      {error ? (
        <p className="text-xs text-fail-400">{error}</p>
      ) : hint ? (
        <p className="text-xs text-muted">{hint}</p>
      ) : null}
    </div>
  );
}
