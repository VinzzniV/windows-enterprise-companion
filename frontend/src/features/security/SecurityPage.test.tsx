import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { LatestScanResult, SecurityFinding } from '../../shared/api-types';
import { SecurityPage } from './SecurityPage';

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
});
