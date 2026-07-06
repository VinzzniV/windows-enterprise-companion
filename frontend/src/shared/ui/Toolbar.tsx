import type { ReactNode } from 'react';

interface ToolbarProps {
  /** Filters/controls on the left. */
  children: ReactNode;
  /** Actions pinned to the right (buttons). */
  actions?: ReactNode;
}

/** Horizontal filter/action row above a result view. Wraps on narrow widths. */
export function Toolbar({ children, actions }: ToolbarProps) {
  return (
    <div className="flex flex-wrap items-center gap-3 rounded-lg border border-slate-800 bg-slate-900/40 px-3 py-2">
      <div className="flex flex-wrap items-center gap-3">{children}</div>
      {actions && <div className="ml-auto flex flex-wrap items-center gap-2">{actions}</div>}
    </div>
  );
}
