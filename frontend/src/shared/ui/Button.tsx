import type { ButtonHTMLAttributes } from 'react';

type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger';

const variantStyles: Record<ButtonVariant, string> = {
  primary:
    'bg-accent-600 text-white hover:bg-accent-500 disabled:hover:bg-accent-600',
  secondary:
    'border border-slate-600 text-slate-200 hover:bg-slate-800 disabled:hover:bg-transparent',
  ghost: 'text-slate-300 hover:bg-slate-800 hover:text-slate-100',
  danger:
    'border border-fail-500 bg-fail-700 text-white hover:bg-fail-600 disabled:hover:bg-fail-700',
};

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
}

/** Shared action hierarchy: primary, secondary/ghost, and confirmed destructive danger actions. */
export function Button({ variant = 'secondary', className = '', type = 'button', ...rest }: ButtonProps) {
  return (
    <button
      type={type}
      className={`cursor-pointer rounded px-3 py-1.5 text-sm font-medium transition-colors duration-150 disabled:cursor-default disabled:opacity-50 ${variantStyles[variant]} ${className}`}
      {...rest}
    />
  );
}
