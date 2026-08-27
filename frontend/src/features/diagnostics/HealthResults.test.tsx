import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { DiagnosticResult, DiagnosticRunResult } from '../../shared/api-types';
import { CategorySections, RunSummary } from './HealthResults';

function diagnostic(overrides: Partial<DiagnosticResult>): DiagnosticResult {
  return {
    diagnosticId: 'WEC-DIAG-TEST',
    title: 'Test diagnostic',
    status: 'PASS',
    category: 'SYSTEM',
    affectedResource: 'Resource',
    evidence: { key: 'value' },
    suggestedNextSteps: [],
    requiredPrivilege: null,
    capturedAtUtc: '2026-07-03T10:00:00Z',
    ...overrides,
  };
}

const run: DiagnosticRunResult = {
  startedAtUtc: '2026-07-03T10:00:00Z',
  completedAtUtc: '2026-07-03T10:00:05Z',
  results: [
    diagnostic({ title: 'Disk space healthy', status: 'PASS' }),
    diagnostic({ title: 'Automatic service stopped', status: 'FAIL', category: 'SERVICES' }),
  ],
};

describe('HealthResults', () => {
  it('orders categories and checks by operational urgency', () => {
    render(<CategorySections results={[
      diagnostic({ title: 'Disk pass', status: 'PASS', category: 'SYSTEM' }),
      diagnostic({ title: 'Service not run', status: 'NOT_RUN', category: 'SERVICES' }),
      diagnostic({ title: 'Event warning', status: 'WARNING', category: 'EVENT_LOG' }),
      diagnostic({ title: 'Update warning', status: 'WARNING', category: 'SYSTEM' }),
      diagnostic({ title: 'Disk failure', status: 'FAIL', category: 'SYSTEM' }),
    ]} />);

    expect(screen.getAllByRole('heading', { level: 2 }).map((heading) => heading.textContent)).toEqual([
      expect.stringContaining('System'),
      expect.stringContaining('Event logs'),
      expect.stringContaining('Services'),
    ]);
    expect(screen.getAllByRole('heading', { level: 3 }).map((heading) => heading.textContent)).toEqual([
      'Disk failure', 'Update warning', 'Disk pass', 'Event warning', 'Service not run',
    ]);
  });

  it('keeps pass evidence collapsed and puts not-run guidance first', () => {
    render(<CategorySections results={[
      diagnostic({ title: 'Healthy adapter', status: 'PASS', evidence: { adapter: 'Ethernet' } }),
      diagnostic({ title: 'Provider unavailable', status: 'NOT_RUN', suggestedNextSteps: ['Restore provider access'] }),
    ]} />);

    expect(screen.getByText('Healthy adapter').closest('li')?.querySelector('details')?.hasAttribute('open')).toBe(false);
    expect(screen.getByText('Provider unavailable').closest('li')?.querySelector('ul')?.textContent).toContain('Restore provider access');
  });

  it('orders summary metrics critical, warning, unknown, then healthy', () => {
    render(<RunSummary results={run.results} />);
    expect(screen.getAllByText(/^(Critical|Warning|Unknown|Healthy)$/).map((label) => label.textContent)).toEqual([
      'Critical', 'Warning', 'Unknown', 'Healthy',
    ]);
  });
});
