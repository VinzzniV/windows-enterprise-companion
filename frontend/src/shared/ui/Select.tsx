import type { SelectHTMLAttributes } from 'react';
import { controlClass } from './Input';

interface SelectProps extends SelectHTMLAttributes<HTMLSelectElement> {
  /** Fill the container (forms) vs. size to content (inline filters). */
  fullWidth?: boolean;
}

/** Native select with the shared control look and a custom chevron. */
export function Select({ className = '', fullWidth = true, children, ...rest }: SelectProps) {
  return (
    <div className={`relative inline-flex items-center ${fullWidth ? 'w-full' : ''}`}>
      <select
        className={`${controlClass} ${fullWidth ? 'w-full' : 'w-auto'} cursor-pointer appearance-none pr-8 ${className}`}
        {...rest}
      >
        {children}
      </select>
      <svg
        viewBox="0 0 16 16"
        aria-hidden="true"
        className="pointer-events-none absolute right-2.5 h-3.5 w-3.5 text-slate-400"
        fill="none"
        stroke="currentColor"
        strokeWidth="1.5"
      >
        <path d="M4 6l4 4 4-4" strokeLinecap="round" strokeLinejoin="round" />
      </svg>
    </div>
  );
}
