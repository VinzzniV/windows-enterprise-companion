import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import {
  SemanticStatusBadge,
  semanticStatusPresentation,
  type SemanticStatus,
} from './SemanticStatusBadge';

const statuses: Array<[SemanticStatus, string, string]> = [
  [{ dimension: 'health', value: 'healthy' }, 'Healthy', 'ok'],
  [{ dimension: 'health', value: 'warning' }, 'Warning', 'warn'],
  [{ dimension: 'health', value: 'critical' }, 'Critical', 'fail'],
  [{ dimension: 'execution', value: 'idle' }, 'Idle', 'neutral'],
  [{ dimension: 'execution', value: 'running' }, 'Running', 'info'],
  [{ dimension: 'execution', value: 'succeeded' }, 'Succeeded', 'ok'],
  [{ dimension: 'execution', value: 'partial' }, 'Partial', 'warn'],
  [{ dimension: 'execution', value: 'failed' }, 'Failed', 'fail'],
  [{ dimension: 'availability', value: 'available' }, 'Available', 'ok'],
  [{ dimension: 'availability', value: 'missing' }, 'Missing', 'warn'],
  [{ dimension: 'availability', value: 'unknown' }, 'Unknown', 'neutral'],
  [{ dimension: 'availability', value: 'not-configured' }, 'Not configured', 'neutral'],
  [{ dimension: 'availability', value: 'not-applicable' }, 'Not applicable', 'neutral'],
  [{ dimension: 'lifecycle', value: 'current' }, 'Current', 'ok'],
  [{ dimension: 'lifecycle', value: 'update-available' }, 'Update available', 'warn'],
  [{ dimension: 'lifecycle', value: 'pending' }, 'Pending', 'warn'],
  [{ dimension: 'lifecycle', value: 'disabled' }, 'Disabled', 'neutral'],
  [{ dimension: 'freshness', value: 'fresh' }, 'Fresh', 'ok'],
  [{ dimension: 'freshness', value: 'stale' }, 'Stale', 'warn'],
];

describe('SemanticStatusBadge', () => {
  it.each(statuses)('renders %j as the canonical label and tone', (status, label, tone) => {
    expect(semanticStatusPresentation(status)).toEqual({ label, tone });
    render(<SemanticStatusBadge status={status} />);
    expect(screen.getByText(label).className).toContain(`border-${tone === 'neutral' ? 'slate' : tone}-`);
  });
});
