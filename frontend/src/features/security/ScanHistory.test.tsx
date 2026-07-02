import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import type { ScanHistoryResult, SecurityFinding } from '../../shared/api-types';
import { ScanHistory } from './ScanHistory';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../shared/bridge/bridgeClient', () => ({ invoke: invokeMock }));

function finding(findingId: string, title: string): SecurityFinding {
  return {
    findingId,
    title,
    description: 'Description',
    severity: 'HIGH',
    category: 'FIREWALL',
    affectedResource: 'Resource',
    evidence: {},
    recommendation: 'Do something',
    requiredPrivilege: null,
    capturedAtUtc: '2026-07-02T18:00:00Z',
  };
}

const history: ScanHistoryResult = {
  scans: [
    {
      scanId: 2,
      startedAtUtc: '2026-07-02T19:00:00Z',
      completedAtUtc: '2026-07-02T19:00:05Z',
      status: 'COMPLETED',
      findingCount: 2,
      severityCounts: [{ severity: 'HIGH', count: 2 }],
    },
    {
      scanId: 1,
      startedAtUtc: '2026-07-02T18:00:00Z',
      completedAtUtc: '2026-07-02T18:00:05Z',
      status: 'COMPLETED',
      findingCount: 2,
      severityCounts: [
        { severity: 'HIGH', count: 1 },
        { severity: 'INFO', count: 1 },
      ],
    },
  ],
  changesSinceLastScan: {
    latestScanId: 2,
    previousScanId: 1,
    newFindings: [finding('NEW', 'SMB1 got enabled')],
    resolvedFindings: [finding('RESOLVED', 'Firewall was re-enabled')],
  },
};

describe('ScanHistory', () => {
  beforeEach(() => {
    invokeMock.mockReset();
  });

  it('renders the diff and the scan rows with a trend sparkline', async () => {
    invokeMock.mockResolvedValue(history);

    render(<ScanHistory refreshToken={2} />);

    expect(await screen.findByText('SMB1 got enabled')).toBeDefined();
    expect(screen.getByText('Firewall was re-enabled')).toBeDefined();
    expect(screen.getByText('Scan history (2)')).toBeDefined();
    expect(screen.getByRole('img', { name: /trend across 2 scans/i })).toBeDefined();
  });

  it('reports an unchanged scan pair as "no changes"', async () => {
    invokeMock.mockResolvedValue({
      ...history,
      changesSinceLastScan: { latestScanId: 2, previousScanId: 1, newFindings: [], resolvedFindings: [] },
    } satisfies ScanHistoryResult);

    render(<ScanHistory refreshToken={2} />);

    expect(await screen.findByText(/No changes/)).toBeDefined();
  });

  it('renders nothing when there is no history yet', async () => {
    invokeMock.mockResolvedValue({ scans: [], changesSinceLastScan: null } satisfies ScanHistoryResult);

    const { container } = render(<ScanHistory refreshToken={null} />);

    await vi.waitFor(() => expect(container.textContent).toBe(''));
  });
});
