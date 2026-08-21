import { Badge, type BadgeTone } from './Badge';

export type SemanticStatus =
  | { dimension: 'health'; value: 'healthy' | 'warning' | 'critical' }
  | { dimension: 'execution'; value: 'idle' | 'running' | 'succeeded' | 'partial' | 'failed' }
  | { dimension: 'availability'; value: 'available' | 'missing' | 'unknown' | 'not-configured' | 'not-applicable' }
  | { dimension: 'lifecycle'; value: 'current' | 'update-available' | 'pending' | 'disabled' }
  | { dimension: 'freshness'; value: 'fresh' | 'stale' };

export interface SemanticStatusPresentation {
  label: string;
  tone: BadgeTone;
}

const health = {
  healthy: { label: 'Healthy', tone: 'ok' },
  warning: { label: 'Warning', tone: 'warn' },
  critical: { label: 'Critical', tone: 'fail' },
} as const satisfies Record<Extract<SemanticStatus, { dimension: 'health' }>['value'], SemanticStatusPresentation>;

const execution = {
  idle: { label: 'Idle', tone: 'neutral' },
  running: { label: 'Running', tone: 'info' },
  succeeded: { label: 'Succeeded', tone: 'ok' },
  partial: { label: 'Partial', tone: 'warn' },
  failed: { label: 'Failed', tone: 'fail' },
} as const satisfies Record<Extract<SemanticStatus, { dimension: 'execution' }>['value'], SemanticStatusPresentation>;

const availability = {
  available: { label: 'Available', tone: 'ok' },
  missing: { label: 'Missing', tone: 'warn' },
  unknown: { label: 'Unknown', tone: 'neutral' },
  'not-configured': { label: 'Not configured', tone: 'neutral' },
  'not-applicable': { label: 'Not applicable', tone: 'neutral' },
} as const satisfies Record<Extract<SemanticStatus, { dimension: 'availability' }>['value'], SemanticStatusPresentation>;

const lifecycle = {
  current: { label: 'Current', tone: 'ok' },
  'update-available': { label: 'Update available', tone: 'warn' },
  pending: { label: 'Pending', tone: 'warn' },
  disabled: { label: 'Disabled', tone: 'neutral' },
} as const satisfies Record<Extract<SemanticStatus, { dimension: 'lifecycle' }>['value'], SemanticStatusPresentation>;

const freshness = {
  fresh: { label: 'Fresh', tone: 'ok' },
  stale: { label: 'Stale', tone: 'warn' },
} as const satisfies Record<Extract<SemanticStatus, { dimension: 'freshness' }>['value'], SemanticStatusPresentation>;

export function semanticStatusPresentation(status: SemanticStatus): SemanticStatusPresentation {
  switch (status.dimension) {
    case 'health': return health[status.value];
    case 'execution': return execution[status.value];
    case 'availability': return availability[status.value];
    case 'lifecycle': return lifecycle[status.value];
    case 'freshness': return freshness[status.value];
  }
}

/** Canonical WEC-owned status: the dimension determines both the visible label and semantic tone. */
export function SemanticStatusBadge({ status }: { status: SemanticStatus }) {
  const presentation = semanticStatusPresentation(status);
  return <Badge tone={presentation.tone}>{presentation.label}</Badge>;
}
