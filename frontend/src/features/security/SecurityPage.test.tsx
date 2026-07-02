import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitForElementToBeRemoved } from '@testing-library/react';
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
    startedAtUtc: '2026-07-02T18:00:00Z',
    completedAtUtc: '2026-07-02T18:00:05Z',
    status: 'COMPLETED',
    findings: [
      finding({ findingId: 'F-HIGH', title: 'Firewall disabled', severity: 'HIGH' }),
      finding({ findingId: 'F-INFO', title: 'Admins documented', severity: 'INFO', category: 'ACCOUNTS' }),
    ],
  },
};

describe('SecurityPage', () => {
  beforeEach(() => {
    invokeMock.mockReset();
  });

  it('renders findings from the latest scan', async () => {
    invokeMock.mockResolvedValue(latestScan);

    render(<SecurityPage />);
    await waitForElementToBeRemoved(() => screen.queryByRole('status'));

    expect(screen.getByText('Firewall disabled')).toBeDefined();
    expect(screen.getByText('Admins documented')).toBeDefined();
  });

  it('hides findings whose severity filter is toggled off', async () => {
    invokeMock.mockResolvedValue(latestScan);

    render(<SecurityPage />);
    await waitForElementToBeRemoved(() => screen.queryByRole('status'));
    await userEvent.click(screen.getByRole('button', { name: 'HIGH' }));

    expect(screen.queryByText('Firewall disabled')).toBeNull();
    expect(screen.getByText('Admins documented')).toBeDefined();
  });

  it('filters by category', async () => {
    invokeMock.mockResolvedValue(latestScan);

    render(<SecurityPage />);
    await waitForElementToBeRemoved(() => screen.queryByRole('status'));
    await userEvent.selectOptions(screen.getByRole('combobox'), 'ACCOUNTS');

    expect(screen.queryByText('Firewall disabled')).toBeNull();
    expect(screen.getByText('Admins documented')).toBeDefined();
  });

  it('shows the empty state when no scan exists yet', async () => {
    invokeMock.mockResolvedValue({ scan: null } satisfies LatestScanResult);

    render(<SecurityPage />);

    expect(await screen.findByText('No scan yet')).toBeDefined();
  });
});
