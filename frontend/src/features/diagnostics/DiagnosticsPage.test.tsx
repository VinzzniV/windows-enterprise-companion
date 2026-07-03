import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { DiagnosticResult, DiagnosticRunResult } from '../../shared/api-types';
import { DiagnosticsPage } from './DiagnosticsPage';

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
  });

  it('shows an error state when the run fails', async () => {
    invokeMock.mockRejectedValue(new Error('bridge down'));

    render(<DiagnosticsPage />);
    await userEvent.click(screen.getByRole('button', { name: 'Run diagnostics' }));

    expect(await screen.findByText('bridge down')).toBeDefined();
  });
});
