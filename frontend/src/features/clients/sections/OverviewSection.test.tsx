import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ItHygieneResult } from '../../../shared/api-types';
import { EnvironmentProvider } from '../../../shared/environment/EnvironmentContext';
import { TargetProvider } from '../../../shared/targets/TargetContext';
import { OverviewSection } from './OverviewSection';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../../shared/bridge/bridgeClient', () => ({
  invoke: invokeMock,
  invokeCancellable: (module: string, action: string, payload: unknown) => ({ requestId: 'request-id', promise: invokeMock(module, action, payload), cancel: vi.fn() }),
  subscribe: vi.fn(() => () => {}),
  BridgeInvokeError: class extends Error {},
  BridgeCancelledError: class extends Error {},
  BridgeTimeoutError: class extends Error {},
  BridgeUnavailableError: class extends Error {},
}));

const result: ItHygieneResult = {
  assessedAtUtc: '2026-08-21T07:00:00Z',
  domainName: 'corp.local',
  sources: {
    activeDirectory: { availability: 'AVAILABLE', error: null },
    kaspersky: { availability: 'AVAILABLE', error: null },
    opsi: { availability: 'AVAILABLE', error: null },
    nessus: { availability: 'AVAILABLE', error: null },
  },
  summary: { total: 1, adComputers: 1, kasperskyComputers: 1, opsiComputers: 1, nessusComputers: 1, healthy: 0, problems: 1, incomplete: 0, stale: 1, missingKaspersky: 0, orphanKaspersky: 0, missingOpsi: 0, orphanOpsi: 0, outdated: 0, missingNessus: 0, staleNessus: 0, nessusCritical: 0, nessusHigh: 0 },
  devices: [{
    computerName: 'PC-42', hostName: 'PC-42.corp.local',
    activeDirectory: { exists: true, enabled: true, dnsHostName: 'PC-42.corp.local', operatingSystem: 'Windows 11', description: null, distinguishedName: null, organizationalUnit: null, lastLogonDate: '2026-08-20T07:00:00Z' },
    kaspersky: { exists: true, lastSeen: '2026-08-20T07:00:00Z', agentVersion: '16.0', kesVersion: '21.25', administrationGroup: 'Clients' },
    opsi: { exists: true, clientId: 'pc-42.corp.local', description: null, depotId: 'depot-1', lastSeen: '2026-05-01T07:00:00Z', clientAgentVersion: '4.3' },
    nessus: { exists: true, assetId: 'asset-1', ipAddress: '10.0.0.42', lastCompletedScanUtc: '2026-08-20T07:00:00Z', critical: 0, high: 0, medium: 0, low: 0, info: 0, ports: [], scanSources: ['Clients'] },
    assessment: { status: 'WARNING', findings: [{ code: 'STALE_OPSI', severity: 'WARNING', message: 'opsi last seen is stale.' }] },
  }],
};

describe('OverviewSection', () => {
  beforeEach(() => {
    invokeMock.mockReset();
    invokeMock.mockImplementation((module: string, action: string) => {
      if (module === 'targets' && action === 'list') return Promise.resolve({ targets: [] });
      if (module === 'employeelifecycle' && action === 'getHygiene') return Promise.resolve(result);
      if (module === 'connectivity' && action === 'probeHosts') return Promise.resolve({ results: [{ host: 'PC-42.corp.local', reachable: true, manageable: true }] });
      return Promise.reject(new Error(`Unexpected action ${module}/${action}`));
    });
  });

  it('shows the opsi card as stale when the shared assessment is stale', async () => {
    render(<TargetProvider><EnvironmentProvider><OverviewSection host="PC-42.corp.local" /></EnvironmentProvider></TargetProvider>);

    await screen.findByText('Client ID');
    const opsiCard = screen.getByText('Client ID').closest('[class*="rounded"]');
    expect(opsiCard).not.toBeNull();
    expect(opsiCard!.textContent).toContain('Stale');
    expect(opsiCard!.textContent).not.toContain('Available');
  });
});
