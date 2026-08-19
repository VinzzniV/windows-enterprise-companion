import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { DiagnosticResult, DiagnosticRunResult } from '../../shared/api-types';
import { CategorySections, DiagnosticsPage, RunSummary } from './DiagnosticsPage';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../shared/bridge/bridgeClient', () => ({
  invoke: invokeMock,
  BridgeInvokeError: class extends Error {},
}));

function diagnostic(overrides: Partial<DiagnosticResult>): DiagnosticResult {
  return {
    diagnosticId: 'WEC-DIAG-TEST',
    title: 'Test diagnostic',
    status: 'PASS',
    category: 'NETWORK',
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
    diagnostic({ title: 'Gateway reachable', status: 'PASS' }),
    diagnostic({
      title: 'DNS server unreachable',
      status: 'FAIL',
      category: 'DNS',
      suggestedNextSteps: ['Check the configured DNS servers'],
    }),
  ],
};

describe('DiagnosticsPage', () => {
  beforeEach(() => {
    invokeMock.mockReset();
  });

  it('shows the status summary and grouped results after a run', async () => {
    invokeMock.mockResolvedValue(run);

    render(<DiagnosticsPage />);
    await userEvent.click(screen.getByRole('button', { name: 'Run diagnostics' }));

    expect(await screen.findByText('Gateway reachable')).toBeDefined();
    expect(screen.getByText('DNS server unreachable')).toBeDefined();
    // Summary strip counts
    expect(screen.getByText('Pass')).toBeDefined();
    expect(screen.getByText('Fail')).toBeDefined();
    // Failing checks put the way forward first
    expect(screen.getByText('Check the configured DNS servers')).toBeDefined();
    const failedRow = screen.getByText('DNS server unreachable').closest('li');
    expect(failedRow?.querySelector('details')?.hasAttribute('open')).toBe(false);
    expect(failedRow?.querySelector('summary')?.textContent).toContain('Raw evidence (1)');
  });

  it('shows an error state when the run fails', async () => {
    invokeMock.mockRejectedValue(new Error('bridge down'));

    render(<DiagnosticsPage />);
    await userEvent.click(screen.getByRole('button', { name: 'Run diagnostics' }));

    expect(await screen.findByText('bridge down')).toBeDefined();
  });

  it('orders categories and checks by operational urgency', () => {
    render(
      <CategorySections
        results={[
          diagnostic({ title: 'Network pass', status: 'PASS', category: 'NETWORK' }),
          diagnostic({ title: 'Domain not run', status: 'NOT_RUN', category: 'DOMAIN' }),
          diagnostic({ title: 'Event warning', status: 'WARNING', category: 'EVENT_LOG' }),
          diagnostic({ title: 'DNS warning', status: 'WARNING', category: 'DNS' }),
          diagnostic({ title: 'DNS failure', status: 'FAIL', category: 'DNS' }),
        ]}
      />,
    );

    expect(screen.getAllByRole('heading', { level: 2 }).map((heading) => heading.textContent)).toEqual([
      expect.stringContaining('DNS'),
      expect.stringContaining('Event logs'),
      expect.stringContaining('Domain'),
      expect.stringContaining('Network'),
    ]);
    expect(screen.getAllByRole('heading', { level: 3 }).map((heading) => heading.textContent)).toEqual([
      'DNS failure',
      'DNS warning',
      'Event warning',
      'Domain not run',
      'Network pass',
    ]);
  });

  it('keeps pass evidence collapsed and puts not-run guidance first', () => {
    render(
      <CategorySections
        results={[
          diagnostic({ title: 'Healthy adapter', status: 'PASS', evidence: { adapter: 'Ethernet' } }),
          diagnostic({
            title: 'Provider unavailable',
            status: 'NOT_RUN',
            suggestedNextSteps: ['Restore provider access'],
          }),
        ]}
      />,
    );

    const healthyRow = screen.getByText('Healthy adapter').closest('li');
    expect(healthyRow?.querySelector('details')?.hasAttribute('open')).toBe(false);
    expect(healthyRow?.querySelector('summary')?.textContent).toContain('Raw evidence (1)');
    const notRunRow = screen.getByText('Provider unavailable').closest('li');
    expect(notRunRow?.querySelector('ul')?.textContent).toContain('Restore provider access');
  });

  it('orders summary metrics fail, warning, not run, then pass', () => {
    render(<RunSummary results={run.results} />);

    expect(screen.getAllByText(/^(Fail|Warning|Not run|Pass)$/).map((label) => label.textContent)).toEqual([
      'Fail',
      'Warning',
      'Not run',
      'Pass',
    ]);
  });
});
