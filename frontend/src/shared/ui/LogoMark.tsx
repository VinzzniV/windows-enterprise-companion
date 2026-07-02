interface LogoMarkProps {
  className?: string;
  animated?: boolean;
}

/**
 * WEC logo mark: hexagonal outline (resilience/enterprise) around three
 * rack bars (systems under management). `animated` enables the stroke-draw
 * and staggered bar reveal used by the splash intro.
 */
export function LogoMark({ className, animated = false }: LogoMarkProps) {
  return (
    <svg viewBox="0 0 64 64" fill="none" className={className} aria-hidden="true">
      <path
        d="M32 4 L55 17 V47 L32 60 L9 47 V17 Z"
        stroke="url(#wec-logo-gradient)"
        strokeWidth="3"
        strokeLinejoin="round"
        className={animated ? 'wec-logo-hex' : undefined}
      />
      {[24, 31, 38].map((y, index) => (
        <rect
          key={y}
          x="21"
          y={y}
          width="22"
          height="4"
          rx="1.5"
          fill="currentColor"
          className={animated ? 'wec-logo-bar' : undefined}
          style={animated ? { animationDelay: `${650 + index * 140}ms` } : undefined}
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
