import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { EnvironmentProvider } from '../../shared/environment/EnvironmentContext';
import { TargetProvider } from '../../shared/targets/TargetContext';
import { VulnerabilitiesPage } from './VulnerabilitiesPage';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));
vi.mock('../../shared/bridge/bridgeClient', () => ({ invoke: invokeMock, BridgeInvokeError: class extends Error {} }));

const sync = { phase: 'COMPLETED', running: false, startedAtUtc: null, lastSuccessfulSyncUtc: '2026-08-18T12:00:00Z', error: null, completedScans: 2, totalScans: 2, historySupported: true, serverVersion: '10.8' };
const environment = { assessedAtUtc: '2026-08-18T12:00:00Z', domainName: 'example.test', sources: { activeDirectory: { availability: 'AVAILABLE', error: null }, kaspersky: { availability: 'AVAILABLE', error: null }, opsi: { availability: 'AVAILABLE', error: null }, nessus: { availability: 'AVAILABLE', error: null } }, summary: { total: 1, adComputers: 1, kasperskyComputers: 1, opsiComputers: 1, nessusComputers: 1, healthy: 0, problems: 1, incomplete: 0, stale: 0, missingKaspersky: 0, orphanKaspersky: 0, missingOpsi: 0, orphanOpsi: 0, outdated: 0, missingNessus: 0, staleNessus: 0, nessusCritical: 1, nessusHigh: 0 }, devices: [] };

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
    expect(screen.getByText(/30-day trend/i)).toBeTruthy();
    await userEvent.click(screen.getByRole('button', { name: 'Assets' }));
    expect(await screen.findByText('Printer')).toBeTruthy();
    expect(screen.getByText(/Unmatched/)).toBeTruthy();
    await userEvent.click(screen.getByRole('button', { name: 'Findings' }));
    expect(await screen.findByText('Critical TLS')).toBeTruthy();
    await userEvent.click(screen.getByRole('button', { name: 'Scans' }));
    expect(await screen.findByText('Clients')).toBeTruthy();
  });

  it('opens a client link directly on findings filtered to that asset', async () => {
    render(<MemoryRouter initialEntries={['/vulnerabilities?tab=findings&asset=156VW']}><TargetProvider><EnvironmentProvider><VulnerabilitiesPage /></EnvironmentProvider></TargetProvider></MemoryRouter>);

    expect(await screen.findByText(/Showing Nessus findings for/)).toBeTruthy();
    expect(screen.getByText('156VW')).toBeTruthy();
    expect(await screen.findByText('Critical TLS')).toBeTruthy();
    expect(invokeMock).toHaveBeenCalledWith('vulnerabilitymanagement', 'listFindings', {
      search: '', severity: null, asset: '156VW', page: 1, pageSize: 250,
    });
  });
});
