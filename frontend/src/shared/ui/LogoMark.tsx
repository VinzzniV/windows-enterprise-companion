interface LogoMarkProps {
  className?: string;
}

/** WEC logo mark: hexagonal outline around three managed-system rack bars. */
export function LogoMark({ className }: LogoMarkProps) {
  return (
    <svg viewBox="0 0 64 64" fill="none" className={className} aria-hidden="true">
      <path
        d="M32 4 L55 17 V47 L32 60 L9 47 V17 Z"
        stroke="url(#wec-logo-gradient)"
        strokeWidth="3"
        strokeLinejoin="round"
      />
      {[24, 31, 38].map((y) => (
        <rect
          key={y}
          x="21"
          y={y}
          width="22"
          height="4"
          rx="1.5"
          fill="currentColor"
        />
      ))}
      <defs>
        <linearGradient id="wec-logo-gradient" x1="9" y1="4" x2="55" y2="60">
          <stop stopColor="#38bdf8" />
          <stop offset="1" stopColor="#6366f1" />
        </linearGradient>
      </defs>
    </svg>
  );
}
