import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { NessusSyncStatus } from '../../shared/api-types';
import { EnvironmentProvider } from '../../shared/environment/EnvironmentContext';
import { TargetProvider } from '../../shared/targets/TargetContext';
import { semanticStatusPresentation } from '../../shared/ui/SemanticStatusBadge';
import { nessusSyncSemanticStatus, VulnerabilitiesPage } from './VulnerabilitiesPage';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));
vi.mock('../../shared/bridge/bridgeClient', () => ({
  invoke: invokeMock,
  invokeCancellable: (module: string, action: string, payload: unknown) => ({ requestId: 'request-id', promise: invokeMock(module, action, payload), cancel: vi.fn() }),
  subscribe: vi.fn(() => () => {}),
  BridgeInvokeError: class extends Error {},
  BridgeCancelledError: class extends Error {},
  BridgeTimeoutError: class extends Error {},
  BridgeUnavailableError: class extends Error {},
}));

const sync: NessusSyncStatus = { phase: 'COMPLETED', running: false, startedAtUtc: null, lastSuccessfulSyncUtc: '2026-08-18T12:00:00Z', error: null, completedScans: 2, totalScans: 2, historySupported: true, serverVersion: '10.8' };
const environment = { assessedAtUtc: '2026-08-18T12:00:00Z', domainName: 'example.test', sources: { activeDirectory: { availability: 'AVAILABLE', error: null }, kaspersky: { availability: 'AVAILABLE', error: null }, opsi: { availability: 'AVAILABLE', error: null }, nessus: { availability: 'AVAILABLE', error: null } }, summary: { total: 1, adComputers: 1, kasperskyComputers: 1, opsiComputers: 1, nessusComputers: 1, healthy: 0, problems: 1, incomplete: 0, stale: 0, missingKaspersky: 0, orphanKaspersky: 0, missingOpsi: 0, orphanOpsi: 0, outdated: 0, missingNessus: 0, staleNessus: 0, nessusCritical: 1, nessusHigh: 0 }, devices: [] };
const syncSemanticCases: Array<[Partial<NessusSyncStatus>, string, string]> = [
  [{ phase: 'IDLE', running: false, error: null }, 'Idle', 'neutral'],
  [{ phase: 'IMPORTING_HISTORY', running: true, error: null }, 'Running', 'info'],
  [{ phase: 'COMPLETED', running: false, error: null }, 'Succeeded', 'ok'],
  [{ phase: 'COMPLETED', running: false, error: 'One scan import failed.' }, 'Partial', 'warn'],
  [{ phase: 'FAILED', running: false, error: 'Sync failed.' }, 'Failed', 'fail'],
  [{ phase: 'IMPORTING_CURRENT_RUNS', running: false, error: null }, 'Unknown', 'neutral'],
];

describe('VulnerabilitiesPage', () => {
  beforeEach(() => {
    invokeMock.mockReset();
    invokeMock.mockImplementation((module: string, action: string) => {
      if (module === 'targets' && action === 'list') return Promise.resolve({ targets: [] });
      if (module === 'employeelifecycle') return Promise.resolve(environment);
      if (action === 'getOverview') return Promise.resolve({ sync, includedScans: 2, excludedScans: 1, assets: 5, matchedAssets: 4, unmatchedAssets: 1, criticalAssets: 1, highAssets: 2, criticalInstances: 2, highInstances: 5, staleScans: 0 });
      if (action === 'listAssets') return Promise.resolve({ items: [{ matched: false, asset: { assetKey: 'IP:10.0.0.9', displayName: 'Printer', hostName: null, fqdn: null, ipAddress: '10.0.0.9', assetId: null, lastScanUtc: '2026-08-18T10:00:00Z', critical: 0, high: 1, medium: 0, low: 0, info: 0, ports: [80], scanSources: ['Network'] } }], total: 1, page: 1, pageSize: 250 });
      if (action === 'listFindings') return Promise.resolve({ items: [{ pluginId: 123, name: 'Critical TLS', severity: 'CRITICAL', cves: ['CVE-2026-1'], affectedAssets: 1, instances: 1 }], total: 1, page: 1, pageSize: 250 });
      if (action === 'listScans') return Promise.resolve([{ id: 1, name: 'Clients', excluded: false, status: 'completed', latestCompletedHistoryId: 9, latestCompletedUtc: '2026-08-18T10:00:00Z', error: null }]);
      if (action === 'getTrend') return Promise.resolve({ verdict: 'BETTER', points: [{ dayUtc: '2026-08-17', critical: 2, high: 3, medium: 4, low: 5, info: 0, assets: 4 }, { dayUtc: '2026-08-18', critical: 1, high: 3, medium: 4, low: 5, info: 0, assets: 5 }], commonAssets: 4, newAssets: 1, removedAssets: 0 });
      return Promise.resolve({ started: false });
    });
  });

  it('shows the overview and the four server-backed views', async () => {
    render(<MemoryRouter><TargetProvider><EnvironmentProvider><VulnerabilitiesPage /></EnvironmentProvider></TargetProvider></MemoryRouter>);
    expect(await screen.findByText('Critical assets')).toBeTruthy();
    expect(screen.getByText('Succeeded').className).toContain('border-ok-');
    expect(screen.queryByText('COMPLETED')).toBeNull();
    expect(screen.getByText(/30-day trend/i)).toBeTruthy();
    await userEvent.click(screen.getByRole('button', { name: 'Assets' }));
    expect(await screen.findByText('Printer')).toBeTruthy();
    expect(screen.getByText(/Unmatched/)).toBeTruthy();
    await userEvent.click(screen.getByRole('button', { name: 'Findings' }));
    expect(await screen.findByText('Critical TLS')).toBeTruthy();
    await userEvent.click(screen.getByRole('button', { name: 'Scans' }));
    expect(await screen.findByText('Clients')).toBeTruthy();
    const scanRow = screen.getByText('ID 1').closest('tr') as HTMLTableRowElement;
    expect(within(scanRow).getByText('Succeeded')).toBeTruthy();
    expect(within(scanRow).queryByText('completed')).toBeNull();
    expect(within(scanRow).getByTitle('Provider status: completed')).toBeTruthy();
  });

  it('replaces an insufficient trend chart with a compact dated empty state', async () => {
    const baseImplementation = invokeMock.getMockImplementation()!;
    invokeMock.mockImplementation((module: string, action: string, payload: Record<string, unknown>) => {
      if (action === 'getTrend') return Promise.resolve({
        verdict: 'INSUFFICIENT_DATA',
        points: [{ dayUtc: '2026-08-18', critical: 2, high: 3, medium: 4, low: 5, info: 0, assets: 4 }],
        commonAssets: 0,
        newAssets: 0,
        removedAssets: 0,
      });
      return baseImplementation(module, action, payload);
    });

    render(<MemoryRouter><TargetProvider><EnvironmentProvider><VulnerabilitiesPage /></EnvironmentProvider></TargetProvider></MemoryRouter>);

    expect(await screen.findByText('Not enough data for a trend yet')).toBeTruthy();
    const earliestDate = screen.getByText('Aug 19, 2026');
    expect(earliestDate.tagName).toBe('TIME');
    expect(earliestDate.getAttribute('datetime')).toBe('2026-08-19');
    expect(screen.getByText(/another snapshot captures at least one previously seen asset/i)).toBeTruthy();
    expect(screen.queryByRole('img', { name: 'Nessus severity trend' })).toBeNull();
  });

  it('opens a client link directly on findings filtered to that asset', async () => {
    render(<MemoryRouter initialEntries={['/vulnerabilities?tab=findings&asset=156VW']}><TargetProvider><EnvironmentProvider><VulnerabilitiesPage /></EnvironmentProvider></TargetProvider></MemoryRouter>);

    expect(await screen.findByText(/Showing Nessus findings for/)).toBeTruthy();
    expect(screen.getByText('156VW')).toBeTruthy();
    expect(await screen.findByText('Critical TLS')).toBeTruthy();
    expect(invokeMock).toHaveBeenCalledWith('vulnerabilitymanagement', 'listFindings', {
      search: '', severity: null, asset: '156VW', page: 1, pageSize: 50,
      sortColumn: 'severity', sortDirection: 'desc',
    });
  });

  it.each(syncSemanticCases)('maps Nessus sync state %j to canonical %s semantics', (override, label, tone) => {
    expect(semanticStatusPresentation(nessusSyncSemanticStatus({ ...sync, ...override }))).toEqual({ label, tone });
  });

  it('requests vulnerability pages and sorting from the server', async () => {
    const baseImplementation = invokeMock.getMockImplementation()!;
    invokeMock.mockImplementation((module: string, action: string, payload: Record<string, unknown>) => {
      if (action === 'listAssets') return Promise.resolve({
        items: [{ matched: false, asset: { assetKey: `IP:10.0.0.${payload.page}`, displayName: 'Printer', hostName: null, fqdn: null, ipAddress: '10.0.0.9', assetId: null, lastScanUtc: '2026-08-18T10:00:00Z', critical: 0, high: 1, medium: 0, low: 0, info: 0, ports: [80], scanSources: ['Network'] } }],
        total: 101, page: payload.page, pageSize: payload.pageSize,
      });
      return baseImplementation(module, action, payload);
    });
    render(<MemoryRouter><TargetProvider><EnvironmentProvider><VulnerabilitiesPage /></EnvironmentProvider></TargetProvider></MemoryRouter>);

    await screen.findByText('Critical assets');
    await userEvent.click(screen.getByRole('button', { name: 'Assets' }));
    expect(await screen.findByText('1–1 of 101')).toBeTruthy();
    expect(invokeMock).toHaveBeenCalledWith('vulnerabilitymanagement', 'listAssets', {
      knownHosts: [], search: '', severity: null, page: 1, pageSize: 50,
      sortColumn: 'critical', sortDirection: 'desc',
    });

    await userEvent.click(screen.getByRole('button', { name: 'Next' }));
    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith('vulnerabilitymanagement', 'listAssets', expect.objectContaining({ page: 2 })));
    await userEvent.click(screen.getByRole('button', { name: /Critical/ }));
    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith('vulnerabilitymanagement', 'listAssets', expect.objectContaining({ page: 1, sortColumn: 'critical', sortDirection: 'asc' })));
  });

  it('keeps overview failures actionable and raw evidence collapsed', async () => {
    const baseImplementation = invokeMock.getMockImplementation()!;
    invokeMock.mockImplementation((module: string, action: string, payload: Record<string, unknown>) => {
      if (action === 'getOverview') return Promise.reject(new Error('overview provider raw failure'));
      return baseImplementation(module, action, payload);
    });

    render(<MemoryRouter><TargetProvider><EnvironmentProvider><VulnerabilitiesPage /></EnvironmentProvider></TargetProvider></MemoryRouter>);

    const alert = await screen.findByRole('alert');
    expect(within(alert).getByText('The Nessus vulnerability overview could not be loaded.')).toBeTruthy();
    expect(within(alert).getByText('Cause')).toBeTruthy();
    expect(within(alert).getByText('Next action')).toBeTruthy();
    expect(within(alert).getByRole('button', { name: 'Retry Nessus overview' })).toBeTruthy();
    const details = within(alert).getByText('Technical details').closest('details') as HTMLDetailsElement;
    expect(details.open).toBe(false);
    await userEvent.click(details.querySelector('summary')!);
    expect(within(alert).getByText(/overview provider raw failure/)).toBeTruthy();
  });

  it('reports a failed manual synchronization and offers a local retry', async () => {
    const baseImplementation = invokeMock.getMockImplementation()!;
    invokeMock.mockImplementation((module: string, action: string, payload: Record<string, unknown>) => {
      if (action === 'getOverview') return Promise.resolve({
        sync: { ...sync, lastSuccessfulSyncUtc: new Date().toISOString() },
        includedScans: 2, excludedScans: 1, assets: 5, matchedAssets: 4, unmatchedAssets: 1,
        criticalAssets: 1, highAssets: 2, criticalInstances: 2, highInstances: 5, staleScans: 0,
      });
      if (action === 'startSync') return Promise.reject(new Error('sync start raw failure'));
      return baseImplementation(module, action, payload);
    });

    render(<MemoryRouter><TargetProvider><EnvironmentProvider><VulnerabilitiesPage /></EnvironmentProvider></TargetProvider></MemoryRouter>);
    await screen.findByText('Critical assets');
    await userEvent.click(screen.getByRole('button', { name: 'Refresh Nessus' }));

    const alert = await screen.findByRole('alert');
    expect(within(alert).getByText('The Nessus synchronization could not be started.')).toBeTruthy();
    expect(within(alert).getByRole('button', { name: 'Retry Nessus synchronization' })).toBeTruthy();
  });

  it('keeps active-tab failures contextual and locally retryable', async () => {
    const baseImplementation = invokeMock.getMockImplementation()!;
    invokeMock.mockImplementation((module: string, action: string, payload: Record<string, unknown>) => {
      if (action === 'listAssets') return Promise.reject(new Error('asset list raw failure'));
      return baseImplementation(module, action, payload);
    });

    render(<MemoryRouter><TargetProvider><EnvironmentProvider><VulnerabilitiesPage /></EnvironmentProvider></TargetProvider></MemoryRouter>);
    await screen.findByText('Critical assets');
    await userEvent.click(screen.getByRole('button', { name: 'Assets' }));

    const alert = await screen.findByRole('alert');
    expect(within(alert).getByText('The Nessus asset list could not be loaded.')).toBeTruthy();
    expect(within(alert).getByRole('button', { name: 'Retry Nessus assets' })).toBeTruthy();
  });

  it('keeps finding rows compact and exposes every detail instance through paging', async () => {
    const baseImplementation = invokeMock.getMockImplementation()!;
    const cves = Array.from({ length: 12 }, (_, index) => `CVE-2026-${String(index + 1).padStart(4, '0')}`);
    const instances = Array.from({ length: 51 }, (_, index) => ({
      assetKey: `ASSET-${String(index + 1).padStart(2, '0')}`,
      pluginId: 123,
      port: 443,
      protocol: 'tcp',
      severity: 'CRITICAL',
      name: 'Critical TLS',
      cves,
      synopsis: 'TLS is vulnerable.',
      solution: 'Install the vendor update.',
      lastObservedUtc: '2026-08-18T10:00:00Z',
      scanSources: ['Clients'],
    }));
    invokeMock.mockImplementation((module: string, action: string, payload: Record<string, unknown>) => {
      if (action === 'listFindings') return Promise.resolve({
        items: [{ pluginId: 123, name: 'Critical TLS', severity: 'CRITICAL', cves, affectedAssets: 51, instances: 51 }],
        total: 1,
        page: 1,
        pageSize: 50,
      });
      if (action === 'getFindingDetails') return Promise.resolve({
        pluginId: 123,
        name: 'Critical TLS',
        severity: 'CRITICAL',
        cves,
        synopsis: 'TLS is vulnerable.',
        solution: 'Install the vendor update.',
        instances,
      });
      return baseImplementation(module, action, payload);
    });

    render(<MemoryRouter initialEntries={['/vulnerabilities?tab=findings']}><TargetProvider><EnvironmentProvider><VulnerabilitiesPage /></EnvironmentProvider></TargetProvider></MemoryRouter>);

    const findingRow = (await screen.findByText('Critical TLS')).closest('tr')!;
    expect(findingRow.textContent).toContain('12 CVEs');
    expect(findingRow.textContent).not.toContain('CVE-2026-0001');

    await userEvent.click(findingRow);
    expect(await screen.findByText('1–50 of 51')).toBeTruthy();
    const details = screen.getByRole('region', { name: 'Finding details' });
    expect(within(details).queryByText('ASSET-51')).toBeNull();

    await userEvent.click(within(details).getByRole('button', { name: 'Next' }));
    expect(await within(details).findByText('ASSET-51')).toBeTruthy();
    expect(within(details).getByText('51–51 of 51')).toBeTruthy();
  });

  it('shows and dismisses a contextual finding-detail error', async () => {
    const baseImplementation = invokeMock.getMockImplementation()!;
    invokeMock.mockImplementation((module: string, action: string, payload: Record<string, unknown>) => {
      if (action === 'getFindingDetails') return Promise.reject(new Error('Detail lookup failed'));
      return baseImplementation(module, action, payload);
    });
    render(<MemoryRouter initialEntries={['/vulnerabilities?tab=findings']}><TargetProvider><EnvironmentProvider><VulnerabilitiesPage /></EnvironmentProvider></TargetProvider></MemoryRouter>);

    await userEvent.click((await screen.findByText('Critical TLS')).closest('tr')!);
    const details = await screen.findByRole('region', { name: 'Finding details' });
    expect(await within(details).findByText('Finding details could not be loaded')).toBeTruthy();
    expect(within(details).getByText('The selected finding details could not be loaded.')).toBeTruthy();
    expect(within(details).getByText('Cause')).toBeTruthy();
    expect(within(details).getByText('Next action')).toBeTruthy();
    expect(within(details).getByRole('button', { name: 'Retry finding details' })).toBeTruthy();
    const technicalDetails = within(details).getByText('Technical details').closest('details') as HTMLDetailsElement;
    expect(technicalDetails.open).toBe(false);
    await userEvent.click(technicalDetails.querySelector('summary')!);
    expect(within(details).getByText(/Detail lookup failed/)).toBeTruthy();

    await userEvent.click(within(details).getByRole('button', { name: 'Close finding details' }));
    expect(screen.queryByRole('region', { name: 'Finding details' })).toBeNull();
  });
});
