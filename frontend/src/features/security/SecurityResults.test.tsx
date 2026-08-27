import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import type { SecurityCoverage, SecurityFinding } from '../../shared/api-types';
import { semanticStatusPresentation } from '../../shared/ui/SemanticStatusBadge';
import {
  FindingCard,
  FindingList,
  ResultContext,
  securityCoverageSemanticStatus,
} from './SecurityResults';

function finding(overrides: Partial<SecurityFinding>): SecurityFinding {
  return {
    findingId: 'WEC-SEC-TEST',
    title: 'Test finding',
    description: 'Description',
    severity: 'HIGH',
    category: 'FIREWALL',
    affectedResource: 'Resource',
    evidence: {},
    recommendation: 'Do something',
    requiredPrivilege: null,
    capturedAtUtc: '2026-07-02T18:00:00Z',
    ...overrides,
  };
}

function coverage(overrides: Partial<SecurityCoverage> = {}): SecurityCoverage {
  return {
    isKnown: true,
    totalChecks: 2,
    succeededChecks: 2,
    failedChecks: 0,
    requiresElevationChecks: 0,
    notApplicableChecks: 0,
    applicableChecks: 2,
    isComplete: true,
    ...overrides,
  };
}

describe('SecurityResults', () => {
  it.each<[Partial<SecurityCoverage>, string, string, string]>([
    [{ isKnown: true, isComplete: true }, 'Available', 'Coverage complete', 'ok'],
    [{ isKnown: true, isComplete: false }, 'Partial', 'Coverage incomplete', 'warn'],
    [{ isKnown: false, isComplete: false }, 'Unknown', 'Coverage unavailable', 'neutral'],
  ])('maps Security coverage with explicit context', (overrides, label, context, tone) => {
    const scanCoverage = coverage(overrides);
    expect(semanticStatusPresentation(securityCoverageSemanticStatus(scanCoverage))).toEqual({ label, tone });
    render(<ResultContext scan={{
      scanId: 1,
      host: 'TESTHOST',
      startedAtUtc: '2026-07-02T18:00:00Z',
      completedAtUtc: '2026-07-02T18:00:05Z',
      status: 'COMPLETED',
      findings: [],
      checkResults: [],
      coverageVersion: 1,
      coverage: scanCoverage,
    }} problemCount={0} />);
    expect(screen.getByText(context)).toBeDefined();
  });

  it('keeps administrator guidance visible and raw evidence collapsed', async () => {
    render(<ul><FindingCard finding={finding({ evidence: { providerCode: '42' } })} /></ul>);
    expect(screen.getByText(/Recommendation:/).parentElement?.textContent).toContain('Do something');
    const disclosure = screen.getByText('Raw evidence (1)').closest('details');
    expect(disclosure?.hasAttribute('open')).toBe(false);
    await userEvent.click(screen.getByText('Raw evidence (1)'));
    expect(screen.getByText('providerCode')).toBeDefined();
  });

  it('filters findings by severity and category', async () => {
    render(<FindingList findings={[
      finding({ findingId: 'F-HIGH', title: 'Firewall disabled', severity: 'HIGH' }),
      finding({ findingId: 'F-INFO', title: 'Admins documented', severity: 'INFO', category: 'ACCOUNTS' }),
    ]} />);

    const highFilter = screen.getByRole('button', { name: 'HIGH: 1 finding' });
    await userEvent.click(highFilter);
    expect(screen.queryByText('Firewall disabled')).toBeNull();
    expect(screen.getByText('Admins documented')).toBeDefined();
    await userEvent.click(highFilter);
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Category filter' }), 'ACCOUNTS');
    expect(screen.queryByText('Firewall disabled')).toBeNull();
    expect(screen.getByText('Admins documented')).toBeDefined();
  });
});
