import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ActionCenterPage as ActionCenterPageResult } from '../../shared/api-types';
import { TargetProvider } from '../../shared/targets/TargetContext';
import { ActionCenterPage } from './ActionCenterPage';

const { invokeMock, cancelMock } = vi.hoisted(() => ({
  invokeMock: vi.fn(),
  cancelMock: vi.fn(),
}));
vi.mock('../../shared/bridge/bridgeClient', () => ({
  invoke: invokeMock,
  invokeCancellable: (module: string, action: string, payload: unknown) => ({
    requestId: 'action-request',
    promise: invokeMock(module, action, payload),
    cancel: cancelMock,
  }),
  subscribe: vi.fn(() => () => {}),
  BridgeInvokeError: class extends Error {},
  BridgeCancelledError: class extends Error {},
  BridgeTimeoutError: class extends Error {},
  BridgeUnavailableError: class extends Error {},
}));

const result: ActionCenterPageResult = {
  items: [
    {
      id: 'hygiene:PC-01:NessusCriticalVulnerabilities',
      subjectType: 'Device',
      subjectKey: 'PC-01',
      device: 'PC-01',
      userObjectId: null,
      userDisplayName: null,
      problemCode: 'NessusCriticalVulnerabilities',
      problem: 'Nessus reports critical vulnerabilities',
      explanation: 'Nessus reports one critical finding instance.',
      source: 'Nessus',
      severity: 'CRITICAL',
      evidenceAtUtc: '2026-08-25T12:00:00Z',
      assessedAtUtc: '2026-08-27T12:00:00Z',
      evidenceAgeDays: 2,
      coverage: 'AVAILABLE',
      reliability: 'High',
      coverageExplanation: 'Nessus inventory was available.',
      recommendedAction: 'Open Vulnerabilities and review the matching stored Nessus evidence.',
      href: '/vulnerabilities?tab=findings&asset=PC-01',
    },
  ],
  total: 1,
  page: 1,
  pageSize: 25,
  summary: { total: 1, critical: 1, high: 0, warning: 0, unknownCoverage: 0 },
  assessedAtUtc: '2026-08-27T12:00:00Z',
  sources: [
    { source: 'Nessus', availability: 'AVAILABLE', explanation: 'Nessus inventory was available.' },
    { source: 'WEC Security', availability: 'TRUNCATED', explanation: 'Only 1000 scans were evaluated.' },
  ],
  itemsTruncated: true,
};

function renderPage() {
  invokeMock.mockImplementation((module: string, action: string) => {
    if (module === 'targets' && action === 'list') return Promise.resolve({ targets: [] });
    if (module === 'actioncenter' && action === 'listItems') return Promise.resolve(result);
    return Promise.resolve({});
  });
  return render(<MemoryRouter><TargetProvider><ActionCenterPage /></TargetProvider></MemoryRouter>);
}

describe('ActionCenterPage', () => {
  beforeEach(() => {
    localStorage.clear();
    invokeMock.mockReset();
    cancelMock.mockReset();
  });

  it('shows a dense read-only work list with evidence age, coverage and deep links', async () => {
    renderPage();

    expect(await screen.findByRole('heading', { name: 'Action Center' })).toBeDefined();
    expect(screen.getByText('Nessus reports critical vulnerabilities')).toBeDefined();
    expect(screen.getByText('2 days old')).toBeDefined();
    expect(screen.getByText('Available · High')).toBeDefined();
    expect(screen.getByRole('link', { name: 'PC-01' }).getAttribute('href')).toBe('/clients/PC-01');
    expect(screen.getByRole('link', { name: 'Inspect evidence →' }).getAttribute('href'))
      .toBe('/vulnerabilities?tab=findings&asset=PC-01');
    expect(screen.getByText(/reached a configured limit/)).toBeDefined();
    expect(screen.getByText('Read-only')).toBeDefined();
    expect(screen.queryByRole('button', { name: /remediate|resolve|assign/i })).toBeNull();

    await userEvent.click(screen.getByRole('button', { name: 'Show context' }));
    expect(screen.getByRole('heading', { name: 'Action context · PC-01' })).toBeDefined();
    expect(screen.getByText('Reported by')).toBeDefined();
    expect(screen.getByText(/high confidence/)).toBeDefined();
    expect(screen.queryByText(/User:/)).toBeNull();
    await userEvent.click(screen.getByRole('button', { name: 'Close context' }));
    expect(screen.queryByRole('heading', { name: 'Action context · PC-01' })).toBeNull();
  });

  it('applies filters, server sorting and explicit force refresh without querying each keystroke', async () => {
    renderPage();
    await screen.findByText('Nessus reports critical vulnerabilities');
    const initialCalls = invokeMock.mock.calls.filter(([, action]) => action === 'listItems').length;

    await userEvent.type(screen.getByRole('searchbox', { name: 'Search Action Center' }), 'PC-01');
    expect(invokeMock.mock.calls.filter(([, action]) => action === 'listItems')).toHaveLength(initialCalls);
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Severity' }), 'CRITICAL');
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Source' }), 'Nessus');
    await userEvent.click(screen.getByRole('button', { name: 'Apply filters' }));

    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith(
      'actioncenter',
      'listItems',
      expect.objectContaining({ search: 'PC-01', severity: 'CRITICAL', source: 'Nessus', page: 1 }),
    ));
    await userEvent.click(within(screen.getByRole('columnheader', { name: /Severity/ })).getByRole('button'));
    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith(
      'actioncenter',
      'listItems',
      expect.objectContaining({ sortField: 'SEVERITY', sortDirection: 'DESCENDING' }),
    ));
    await userEvent.click(screen.getByRole('button', { name: 'Refresh sources' }));
    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith(
      'actioncenter',
      'listItems',
      expect.objectContaining({ force: true }),
    ));
  });
});
