import type { ButtonHTMLAttributes } from 'react';

type ButtonVariant = 'primary' | 'secondary' | 'ghost';

const variantStyles: Record<ButtonVariant, string> = {
  primary:
    'bg-sky-700 text-slate-50 hover:bg-sky-600 disabled:hover:bg-sky-700',
  secondary:
    'border border-slate-600 text-slate-200 hover:bg-slate-800 disabled:hover:bg-transparent',
  ghost: 'text-slate-300 hover:bg-slate-800 hover:text-slate-100',
};

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
}

/** The one button style: primary = the page's main action, secondary = everything else. */
export function Button({ variant = 'secondary', className = '', type = 'button', ...rest }: ButtonProps) {
  return (
    <button
      type={type}
      className={`cursor-pointer rounded px-3 py-1.5 text-sm font-medium transition-colors duration-150 disabled:cursor-default disabled:opacity-50 ${variantStyles[variant]} ${className}`}
      {...rest}
    />
  );
}
