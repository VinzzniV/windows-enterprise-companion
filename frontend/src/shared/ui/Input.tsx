import type { InputHTMLAttributes } from 'react';

/** Canonical dark form-control look (no width), shared by Input, Select and textareas. */
export const controlClass =
  'rounded border border-slate-700 bg-slate-900 px-2.5 py-1.5 text-sm text-slate-100 ' +
  'placeholder:text-muted transition-colors focus:border-accent-500 focus:outline-none ' +
  'disabled:cursor-not-allowed disabled:opacity-50';

interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  invalid?: boolean;
}

/** Single-line text/password/number input. Fills its container unless width is overridden. */
export function Input({ invalid, className = '', ...rest }: InputProps) {
  return (
    <input
      className={`${controlClass} w-full ${invalid ? 'border-fail-600 focus:border-fail-500' : ''} ${className}`}
      aria-invalid={invalid || undefined}
      {...rest}
    />
  );
}
