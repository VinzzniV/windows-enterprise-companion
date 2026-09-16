import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { DeviceCleanupAssessment, DeviceCleanupPage as DeviceCleanupPageResult } from '../../shared/api-types';
import { TargetProvider } from '../../shared/targets/TargetContext';
import { DeviceCleanupPage } from './DeviceCleanupPage';

const { invokeMock, cancelMock } = vi.hoisted(() => ({
  invokeMock: vi.fn(),
  cancelMock: vi.fn(),
}));

vi.mock('../../shared/bridge/bridgeClient', () => ({
  invoke: invokeMock,
  invokeCancellable: (module: string, action: string, payload: unknown) => ({
    requestId: 'cleanup-request',
    promise: invokeMock(module, action, payload),
    cancel: cancelMock,
  }),
  subscribe: vi.fn(() => () => {}),
  BridgeInvokeError: class extends Error {},
  BridgeCancelledError: class extends Error {},
  BridgeTimeoutError: class extends Error {},
  BridgeUnavailableError: class extends Error {},
}));

const candidate = {
  canTargetWindows: true,
  subjectKey: 'PC-OLD',
  host: 'pc-old.corp.example',
  description: 'Accounting workstation',
  descriptionSource: 'Active Directory',
  classification: 'POTENTIAL_CLEANUP' as const,
  classificationExplanation: 'AD activity exceeds its cleanup threshold; manual review is still required.',
  activeDirectoryExists: true,
  activeDirectoryEnabled: false,
  activeDirectoryLastLogonAtUtc: '2026-01-01T00:00:00Z',
  kasperskyExists: true,
  kasperskyLastSeenAtUtc: '2026-02-01T00:00:00Z',
  opsiExists: true,
  opsiLastSeenAtUtc: '2026-02-02T00:00:00Z',
  nessusExists: true,
  nessusLastScanAtUtc: '2026-02-03T00:00:00Z',
  inventoryExists: true,
  inventoryCapturedAtUtc: '2026-02-04T00:00:00Z',
  relevantFindingCount: 4,
};

const assessment: DeviceCleanupAssessment = {
  candidate,
  sources: [
    { source: 'Active Directory', coverage: 'AVAILABLE', exists: true, state: 'Disabled', observedAtUtc: candidate.activeDirectoryLastLogonAtUtc, explanation: 'Directory evidence is available.' },
    { source: 'Kaspersky', coverage: 'AVAILABLE', exists: true, state: 'Registered', observedAtUtc: candidate.kasperskyLastSeenAtUtc, explanation: 'Kaspersky evidence is available.' },
    { source: 'opsi', coverage: 'AVAILABLE', exists: true, state: 'Registered', observedAtUtc: candidate.opsiLastSeenAtUtc, explanation: 'opsi evidence is available.' },
    { source: 'Nessus', coverage: 'AVAILABLE', exists: true, state: 'Observed', observedAtUtc: candidate.nessusLastScanAtUtc, explanation: 'Nessus evidence is available.' },
    { source: 'WEC Inventory', coverage: 'AVAILABLE', exists: true, state: 'Stored snapshot', observedAtUtc: candidate.inventoryCapturedAtUtc, explanation: 'Stored evidence only.' },
  ],
  findings: [{ code: 'StaleAd', severity: 'Critical', message: 'AD activity is stale.' }],
  userEvidenceAvailability: 'AVAILABLE',
  userEvidenceExplanation: 'One approved observation is available.',
  userObservations: [{
    relationshipType: 'LastInteractiveUser',
    sid: 'S-1-5-21-1-2-3-1104',
    accountDisplay: 'CORP\\alex',
    observedAtUtc: '2026-02-04T00:00:00Z',
    profileLastUseAtUtc: null,
    confidence: 'High',
    explanation: 'Observation only; not ownership.',
  }],
};

function result(selectedAssessment: DeviceCleanupAssessment | null): DeviceCleanupPageResult {
  return {
    candidates: [candidate],
    total: 1,
    page: 1,
    pageSize: 25,
    assessedAtUtc: '2026-08-27T12:00:00Z',
    sources: [
      { source: 'Active Directory', availability: 'AVAILABLE', explanation: 'Available.' },
      { source: 'Kaspersky', availability: 'AVAILABLE', explanation: 'Available.' },
      { source: 'opsi', availability: 'AVAILABLE', explanation: 'Available.' },
      { source: 'Nessus', availability: 'AVAILABLE', explanation: 'Available.' },
      { source: 'WEC Inventory', availability: 'AVAILABLE', explanation: 'One snapshot.' },
    ],
    selectedAssessment,
    subjectsTruncated: false,
  };
}

function renderPage() {
  invokeMock.mockImplementation((module: string, action: string, payload: Record<string, unknown>) => {
    if (module === 'targets' && action === 'list') return Promise.resolve({ targets: [] });
    if (module === 'devicecleanup' && action === 'listCandidates') {
      return Promise.resolve(result(payload.selectedHost ? assessment : null));
    }
    if (module === 'connectivity' && action === 'probeHosts') {
      return Promise.resolve({ results: [{ host: candidate.host, reachable: false, manageable: false }] });
    }
    if (module === 'devicecleanup' && action === 'exportAssessment') {
      return Promise.resolve({ cancelled: false, filePath: 'C:\\Exports\\cleanup.md' });
    }
    if (module === 'devicecleanup' && action === 'exportWorkbook') {
      return Promise.resolve({
        cancelled: false,
        filePath: 'C:\\Exports\\device-cleanup.xlsx',
        exportedCount: 1,
        subjectsTruncated: false,
      });
    }
    return Promise.resolve({});
  });
  return render(<MemoryRouter initialEntries={['/cleanup']}><TargetProvider><DeviceCleanupPage /></TargetProvider></MemoryRouter>);
}

describe('DeviceCleanupPage', () => {
  beforeEach(() => {
    candidate.canTargetWindows = true;
    localStorage.clear();
    invokeMock.mockReset();
    cancelMock.mockReset();
  });

  it('guides a read-only session decision and probes connectivity only on request', async () => {
    const user = userEvent.setup();
    renderPage();

    expect(await screen.findByRole('heading', { name: 'Device Cleanup' })).toBeDefined();
    expect(screen.getByText('Assessment, not deletion')).toBeDefined();
    expect(screen.getByText('No AD writes')).toBeDefined();
    expect(screen.getByText('Potential cleanup')).toBeDefined();
    expect(invokeMock.mock.calls.filter(([module]) => module === 'connectivity')).toHaveLength(0);

    await user.click(screen.getByRole('button', { name: 'Review evidence' }));
    expect(await screen.findByRole('heading', { name: `Review ${candidate.host}` })).toBeDefined();
    expect(screen.getByText('CORP\\alex')).toBeDefined();
    expect(screen.getByText('Not checked')).toBeDefined();
    expect(invokeMock.mock.calls.filter(([module]) => module === 'connectivity')).toHaveLength(0);

    await user.click(screen.getByRole('button', { name: 'Check Ping and WinRM' }));
    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith(
      'connectivity',
      'probeHosts',
      { hosts: [candidate.host] },
    ));
    expect(await screen.findByText('No ping or WinRM response')).toBeDefined();

    const decisionSection = screen.getByRole('heading', { name: 'Manual session decision' }).closest('section')!;
    const exportButton = within(decisionSection).getByRole('button', { name: 'Export assessment' });
    expect(exportButton.hasAttribute('disabled')).toBe(true);
    await user.selectOptions(within(decisionSection).getByRole('combobox'), 'PREPARE_CLEANUP');
    expect(exportButton.hasAttribute('disabled')).toBe(true);
    await user.type(within(decisionSection).getByRole('textbox', { name: 'Required decision reason' }), 'Replacement confirmed.');
    expect(exportButton.hasAttribute('disabled')).toBe(false);
    await user.click(exportButton);

    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith(
      'devicecleanup',
      'exportAssessment',
      expect.objectContaining({ markdown: expect.stringContaining('Required reason: Replacement confirmed.') }),
    ));
    expect(await screen.findByText('Exported to C:\\Exports\\cleanup.md')).toBeDefined();
    expect(screen.queryByRole('button', { name: /disable|delete|move/i })).toBeNull();
  });

  it('applies bounded search explicitly and does not query for each keystroke', async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText(candidate.host);
    const initialCalls = invokeMock.mock.calls.filter(([, action]) => action === 'listCandidates').length;

    await user.type(screen.getByRole('searchbox', { name: 'Search cleanup candidates' }), 'PC-OLD');
    expect(invokeMock.mock.calls.filter(([, action]) => action === 'listCandidates')).toHaveLength(initialCalls);
    await user.click(screen.getByRole('button', { name: 'Apply search' }));

    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith(
      'devicecleanup',
      'listCandidates',
      expect.objectContaining({ search: 'PC-OLD', page: 1, pageSize: 25 }),
    ));
  });

  it('selects the evidence key and disables connectivity for an unresolved source identity', async () => {
    candidate.canTargetWindows = false;
    renderPage();
    await screen.findByRole('button', { name: 'Review evidence' });
    await userEvent.click(screen.getByRole('button', { name: 'Review evidence' }));
    await screen.findByRole('heading', { name: `Review ${candidate.host}` });
    expect((screen.getByRole('button', { name: 'Check Ping and WinRM' }) as HTMLButtonElement).disabled).toBe(true);
    expect(invokeMock).toHaveBeenCalledWith('devicecleanup', 'listCandidates', expect.objectContaining({ selectedHost: candidate.subjectKey }));
    expect(invokeMock.mock.calls.filter(([module]) => module === 'connectivity')).toHaveLength(0);
    expect(screen.getByText(/including Excel Ping, are skipped/)).toBeTruthy();
  });

  it('exports every current-filter result and explicitly requests the bounded Ping workbook', async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText(candidate.host);

    await user.click(screen.getByRole('button', { name: 'Export Excel with Ping' }));

    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith(
      'devicecleanup',
      'exportWorkbook',
      expect.objectContaining({
        search: null,
        includeWithoutSignals: false,
      }),
    ));
    expect(await screen.findByText('Exported all 1 matching devices to C:\\Exports\\device-cleanup.xlsx.')).toBeDefined();
  });
});
