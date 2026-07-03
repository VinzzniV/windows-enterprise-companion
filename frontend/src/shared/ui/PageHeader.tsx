import type { ReactNode } from 'react';

interface PageHeaderProps {
  title: string;
  subtitle: string;
  /** Actions rendered on the right (buttons, status text). */
  children?: ReactNode;
}

/** Uniform page top: title + one-line purpose left, primary actions right. */
export function PageHeader({ title, subtitle, children }: PageHeaderProps) {
  return (
    <header className="flex flex-wrap items-end justify-between gap-3">
      <div className="min-w-0">
        <h1 className="text-xl font-semibold">{title}</h1>
        <p className="text-sm text-slate-400">{subtitle}</p>
      </div>
      {children && <div className="flex shrink-0 flex-wrap items-center gap-3">{children}</div>}
    </header>
  );
}
