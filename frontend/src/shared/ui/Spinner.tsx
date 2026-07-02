interface SpinnerProps {
  label: string;
}

/** Consistent inline loading indicator; the label keeps it screen-reader friendly. */
export function Spinner({ label }: SpinnerProps) {
  return (
    <span className="inline-flex items-center gap-2 text-sm text-slate-400" role="status">
      <svg viewBox="0 0 16 16" className="h-4 w-4 animate-spin" aria-hidden="true">
        <circle
          cx="8"
          cy="8"
          r="6.5"
          fill="none"
          stroke="currentColor"
          strokeOpacity="0.25"
          strokeWidth="2.5"
        />
        <path
          d="M14.5 8a6.5 6.5 0 0 0-6.5-6.5"
          fill="none"
          stroke="currentColor"
          strokeWidth="2.5"
          strokeLinecap="round"
        />
      </svg>
      {label}
    </span>
  );
}
