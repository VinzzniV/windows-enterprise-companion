import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { DiagnosticResult, DiagnosticRunResult } from '../../shared/api-types';
import { DiagnosticsPage } from './DiagnosticsPage';
import { CategorySections, RunSummary } from './HealthResults';

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
    diagnostic({
      title: 'Automatic service stopped',
      status: 'FAIL',
      category: 'SERVICES',
      suggestedNextSteps: ['Check the service configuration'],
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
    await userEvent.click(screen.getByRole('button', { name: 'Run health checks' }));

    expect(await screen.findByText('Disk space healthy')).toBeDefined();
    expect(screen.getByText('Automatic service stopped')).toBeDefined();
    // Summary strip counts
    expect(screen.getAllByText('Healthy').length).toBeGreaterThanOrEqual(2);
    expect(screen.getAllByText('Critical').length).toBeGreaterThanOrEqual(2);
    // Failing checks put the way forward first
    expect(screen.getByText('Check the service configuration')).toBeDefined();
    const failedRow = screen.getByText('Automatic service stopped').closest('li');
    expect(failedRow?.querySelector('details')?.hasAttribute('open')).toBe(false);
    expect(failedRow?.querySelector('summary')?.textContent).toContain('Raw evidence (1)');
  });

  it('shows an error state when the run fails', async () => {
    invokeMock.mockRejectedValue(new Error('bridge down'));

    render(<DiagnosticsPage />);
    await userEvent.click(screen.getByRole('button', { name: 'Run health checks' }));

    expect(await screen.findByText('bridge down')).toBeDefined();
  });

  it('orders categories and checks by operational urgency', () => {
    render(
      <CategorySections
        results={[
          diagnostic({ title: 'Disk pass', status: 'PASS', category: 'SYSTEM' }),
          diagnostic({ title: 'Service not run', status: 'NOT_RUN', category: 'SERVICES' }),
          diagnostic({ title: 'Event warning', status: 'WARNING', category: 'EVENT_LOG' }),
          diagnostic({ title: 'Update warning', status: 'WARNING', category: 'SYSTEM' }),
          diagnostic({ title: 'Disk failure', status: 'FAIL', category: 'SYSTEM' }),
        ]}
      />,
    );

    expect(screen.getAllByRole('heading', { level: 2 }).map((heading) => heading.textContent)).toEqual([
      expect.stringContaining('System'),
      expect.stringContaining('Event logs'),
      expect.stringContaining('Services'),
    ]);
    expect(screen.getAllByRole('heading', { level: 3 }).map((heading) => heading.textContent)).toEqual([
      'Disk failure',
      'Update warning',
      'Disk pass',
      'Event warning',
      'Service not run',
    ]);
    expect(screen.getByText('Critical').className).toContain('border-fail-700');
    expect(screen.getAllByText('Warning').every((badge) => badge.className.includes('border-warn-700'))).toBe(true);
    expect(screen.getByText('Unknown').className).toContain('border-slate-700');
    expect(screen.getByText('Healthy').className).toContain('border-ok-700');
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

  it('orders summary metrics critical, warning, unknown, then healthy', () => {
    render(<RunSummary results={run.results} />);

    expect(screen.getAllByText(/^(Critical|Warning|Unknown|Healthy)$/).map((label) => label.textContent)).toEqual([
      'Critical',
      'Warning',
      'Unknown',
      'Healthy',
    ]);
  });
});
