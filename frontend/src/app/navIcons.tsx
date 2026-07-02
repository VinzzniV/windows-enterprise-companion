import type { ReactNode } from 'react';

const iconProps = {
  viewBox: '0 0 24 24',
  fill: 'none',
  stroke: 'currentColor',
  strokeWidth: 1.8,
  strokeLinecap: 'round',
  strokeLinejoin: 'round',
  className: 'h-4 w-4 shrink-0',
  'aria-hidden': true,
} as const;

/** Lucide-style outline icons for the sidebar navigation. */
export const navIcons: Record<string, ReactNode> = {
  inventory: (
    <svg {...iconProps}>
      <rect x="3" y="4" width="18" height="8" rx="2" />
      <rect x="3" y="14" width="18" height="6" rx="2" />
      <path d="M7 8h.01M7 17h.01" />
    </svg>
  ),
  security: (
    <svg {...iconProps}>
      <path d="M12 3l7 3v5c0 4.5-3 8.5-7 10-4-1.5-7-5.5-7-10V6z" />
      <path d="M9.5 11.5l2 2 3.5-4" />
    </svg>
  ),
  diagnostics: (
    <svg {...iconProps}>
      <path d="M3 12h4l3-8 4 16 3-8h4" />
    </svg>
  ),
  activedirectory: (
    <svg {...iconProps}>
      <circle cx="12" cy="6" r="3" />
      <circle cx="5" cy="18" r="3" />
      <circle cx="19" cy="18" r="3" />
      <path d="M10.5 8.5L6.5 15.5M13.5 8.5l4 7M8 18h8" />
    </svg>
  ),
  reporting: (
    <svg {...iconProps}>
      <path d="M6 3h9l4 4v14H6z" />
      <path d="M15 3v4h4M10 13v4M13 11v6M16 15v2" />
    </svg>
  ),
};
