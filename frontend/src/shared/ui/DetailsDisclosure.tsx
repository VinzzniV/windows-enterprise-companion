import type { ReactNode } from 'react';

interface DetailsDisclosureProps {
  summary: ReactNode;
  children: ReactNode;
  defaultOpen?: boolean;
}

/** Styled native <details>: key values stay visible, bulk detail collapses. */
export function DetailsDisclosure({ summary, children, defaultOpen }: DetailsDisclosureProps) {
  return (
    <details className="group" open={defaultOpen}>
      <summary className="flex cursor-pointer select-none items-center gap-1.5 text-xs text-slate-400 transition-colors hover:text-slate-200">
        <svg
          viewBox="0 0 16 16"
          className="h-3 w-3 shrink-0 transition-transform group-open:rotate-90"
          fill="none"
          stroke="currentColor"
          strokeWidth="1.5"
          aria-hidden="true"
        >
          <path d="M6 4l4 4-4 4" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
        {summary}
      </summary>
      <div className="mt-2">{children}</div>
    </details>
  );
}
