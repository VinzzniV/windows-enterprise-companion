import type { InputHTMLAttributes, ReactNode } from 'react';

interface CheckboxProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'type'> {
  label: ReactNode;
}

/** Checkbox with an inline label; the whole row is the click target. */
export function Checkbox({ label, className = '', disabled, ...rest }: CheckboxProps) {
  return (
    <label
      className={`flex items-center gap-2 text-sm ${
        disabled ? 'text-slate-500' : 'cursor-pointer text-slate-300'
      } ${className}`}
    >
      <input
        type="checkbox"
        disabled={disabled}
        className="h-4 w-4 cursor-pointer accent-accent-500 disabled:cursor-not-allowed"
        {...rest}
      />
      {label}
    </label>
  );
}
