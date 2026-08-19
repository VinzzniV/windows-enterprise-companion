import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type {
  LatestScanResult,
  SecurityCheckResult,
  SecurityCoverage,
  SecurityFinding,
} from '../../shared/api-types';
import { FindingCard, SecurityPage } from './SecurityPage';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../shared/bridge/bridgeClient', () => ({
  invoke: invokeMock,
  BridgeInvokeError: class extends Error {},
}));

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

function checkResult(overrides: Partial<SecurityCheckResult> = {}): SecurityCheckResult {
  return {
    checkId: 'TEST-CHECK',
    status: 'SUCCEEDED',
    findings: [],
    failure: null,
    ...overrides,
  };
}

const latestScan: LatestScanResult = {
  scan: {
    scanId: 1,
    host: 'TESTHOST',
    startedAtUtc: '2026-07-02T18:00:00Z',
    completedAtUtc: '2026-07-02T18:00:05Z',
    status: 'COMPLETED',
    findings: [
      finding({ findingId: 'F-HIGH', title: 'Firewall disabled', severity: 'HIGH' }),
      finding({ findingId: 'F-INFO', title: 'Admins documented', severity: 'INFO', category: 'ACCOUNTS' }),
    ],
    checkResults: [checkResult({ checkId: 'CHECK-A' }), checkResult({ checkId: 'CHECK-B' })],
    coverageVersion: 1,
    coverage: coverage(),
  },
};

/** Routes getLatestScan/getScanHistory separately — the page invokes both. */
function setUpInvoke(latest: LatestScanResult): void {
  invokeMock.mockImplementation((_module: string, action: string) =>
    action === 'getScanHistory'
      ? Promise.resolve({ scans: [], changesSinceLastScan: null })
      : Promise.resolve(latest),
  );
}

describe('SecurityPage', () => {
  beforeEach(() => {
    invokeMock.mockReset();
  });

  it('keeps administrator guidance visible and raw evidence collapsed', async () => {
    render(
      <ul>
        <FindingCard finding={finding({ evidence: { providerCode: '42' } })} />
      </ul>,
    );

    expect(screen.getByText(/Recommendation:/).parentElement?.textContent).toContain('Do something');
    const disclosure = screen.getByText('Raw evidence (1)').closest('details');
    expect(disclosure?.hasAttribute('open')).toBe(false);
    await userEvent.click(screen.getByText('Raw evidence (1)'));
    expect(disclosure?.hasAttribute('open')).toBe(true);
    expect(screen.getByText('providerCode')).toBeDefined();
  });

  it('renders findings from the latest scan', async () => {
    setUpInvoke(latestScan);

    render(<SecurityPage />);

    // Wait for content instead of spinner removal: the page and the history
    // section each render a role="status" spinner, and their resolution
    // order races on slow CI runners
    expect(await screen.findByText('Firewall disabled')).toBeDefined();
    expect(screen.getByText('Admins documented')).toBeDefined();
  });

  it('hides findings whose severity filter is toggled off', async () => {
    setUpInvoke(latestScan);

    render(<SecurityPage />);
    await screen.findByText('Firewall disabled');
    await userEvent.click(screen.getByRole('button', { name: 'HIGH' }));

    expect(screen.queryByText('Firewall disabled')).toBeNull();
    expect(screen.getByText('Admins documented')).toBeDefined();
  });

  it('filters by category', async () => {
    setUpInvoke(latestScan);

    render(<SecurityPage />);
    await screen.findByText('Firewall disabled');
    await userEvent.selectOptions(screen.getByRole('combobox'), 'ACCOUNTS');

    expect(screen.queryByText('Firewall disabled')).toBeNull();
    expect(screen.getByText('Admins documented')).toBeDefined();
  });

  it('hides results from the previous target when the selection changes', async () => {
    setUpInvoke(latestScan);

    render(<SecurityPage />);
    await screen.findByText('Firewall disabled');
    await userEvent.click(screen.getByRole('radio', { name: 'Remote computer' }));

    expect(screen.queryByText('Firewall disabled')).toBeNull();
    expect(screen.getByText('No results for this target yet')).toBeDefined();
  });

  it('shows the empty state when no scan exists yet', async () => {
    setUpInvoke({ scan: null });

    render(<SecurityPage />);

    expect(await screen.findByText('No scan yet')).toBeDefined();
  });

  it('shows failed check outcomes separately from observed findings', async () => {
    setUpInvoke({
      scan: {
        ...latestScan.scan!,
        status: 'COMPLETED_WITH_ERRORS',
        findings: [finding({ findingId: 'F-HIGH', title: 'Firewall disabled', severity: 'HIGH' })],
        checkResults: [
          checkResult({ checkId: 'CHECK-A' }),
          checkResult({
            checkId: 'CHECK-B',
            status: 'REQUIRES_ELEVATION',
            failure: {
              code: 'ACCESS_DENIED',
              message: 'Administrator access is required.',
              requiredPrivilege: 'ADMINISTRATOR',
            },
          }),
        ],
        coverage: coverage({
          succeededChecks: 1,
          requiresElevationChecks: 1,
          isComplete: false,
        }),
      },
    });

    render(<SecurityPage />);
    await screen.findByText('Firewall disabled');

    expect(screen.getByText('Coverage (1 checks not evaluated)')).toBeDefined();
    expect(screen.getByText('Requires elevation')).toBeDefined();
    expect(screen.getByText(/1 finding\b/)).toBeDefined();
  });

  it('never presents an all-failed zero-finding scan as pass', async () => {
    setUpInvoke({
      scan: {
        ...latestScan.scan!,
        status: 'COMPLETED_WITH_ERRORS',
        findings: [],
        checkResults: [
          checkResult({
            checkId: 'CHECK-A',
            status: 'FAILED',
            failure: { code: 'WMI_UNAVAILABLE', message: 'Provider unavailable.', requiredPrivilege: null },
          }),
        ],
        coverage: coverage({
          totalChecks: 1,
          applicableChecks: 1,
          succeededChecks: 0,
          failedChecks: 1,
          isComplete: false,
        }),
      },
    });

    render(<SecurityPage />);

    expect(await screen.findByText('No findings observed — scan coverage is incomplete.')).toBeDefined();
    expect(screen.queryByText(/all applicable checks completed/i)).toBeNull();
  });
});
