import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ItHygieneResult } from '../../shared/api-types';
import { EnvironmentProvider } from '../../shared/environment/EnvironmentContext';
import { TargetProvider } from '../../shared/targets/TargetContext';
import { ClientsPage } from './ClientsPage';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../shared/bridge/bridgeClient', () => ({
  invoke: invokeMock,
  BridgeInvokeError: class extends Error {},
}));

const environment: ItHygieneResult = {
  assessedAtUtc: '2026-08-18T06:00:00Z', domainName: 'corp.local',
  sources: {
    activeDirectory: { availability: 'AVAILABLE', error: null },
    kaspersky: { availability: 'AVAILABLE', error: null },
    opsi: { availability: 'AVAILABLE', error: null },
    nessus: { availability: 'AVAILABLE', error: null },
  },
  summary: { total: 2, adComputers: 2, kasperskyComputers: 1, opsiComputers: 1, nessusComputers: 1, healthy: 2, problems: 0, incomplete: 0, stale: 0, missingKaspersky: 0, orphanKaspersky: 0, missingOpsi: 0, orphanOpsi: 0, outdated: 0, missingNessus: 0, staleNessus: 0, nessusCritical: 0, nessusHigh: 0 },
  devices: [
    {
      computerName: 'DISABLED-PC', hostName: 'disabled-pc.corp.local',
      activeDirectory: { exists: true, enabled: false, dnsHostName: 'disabled-pc.corp.local', operatingSystem: 'Windows 11 Pro', description: null, distinguishedName: 'CN=DISABLED-PC,DC=corp,DC=local', organizationalUnit: 'DC=corp,DC=local', lastLogonDate: null },
      kaspersky: { exists: false, lastSeen: null, agentVersion: null, kesVersion: null, administrationGroup: null },
      opsi: { exists: false, clientId: null, description: null, depotId: null, lastSeen: null, clientAgentVersion: null },
      nessus: { exists: false, assetId: null, ipAddress: null, lastCompletedScanUtc: null, critical: 0, high: 0, medium: 0, low: 0, info: 0, ports: [], scanSources: [] },
      assessment: { status: 'HEALTHY', findings: [] },
    },
    {
      computerName: 'PC01', hostName: 'pc01.corp.local',
      activeDirectory: { exists: true, enabled: true, dnsHostName: 'pc01.corp.local', operatingSystem: 'Windows 11 Pro', description: null, distinguishedName: 'CN=PC01,DC=corp,DC=local', organizationalUnit: 'DC=corp,DC=local', lastLogonDate: null },
      kaspersky: { exists: true, lastSeen: null, agentVersion: '16.0', kesVersion: '21.25', administrationGroup: 'Clients' },
      opsi: { exists: true, clientId: 'pc01.corp.local', description: null, depotId: 'depot01', lastSeen: null, clientAgentVersion: '4.3.8' },
      nessus: { exists: true, assetId: 'asset-1', ipAddress: '10.0.0.1', lastCompletedScanUtc: '2026-08-18T05:00:00Z', critical: 0, high: 0, medium: 1, low: 2, info: 0, ports: [443], scanSources: ['Clients'] },
      assessment: { status: 'HEALTHY', findings: [] },
    },
  ],
};

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/clients']}>
      <TargetProvider>
        <EnvironmentProvider>
          <Routes>
            <Route path="/clients" element={<ClientsPage />} />
            <Route path="/clients/:host" element={<div>Shared client detail</div>} />
          </Routes>
        </EnvironmentProvider>
      </TargetProvider>
    </MemoryRouter>,
  );
}

describe('ClientsPage', () => {
  beforeEach(() => {
    invokeMock.mockReset();
    invokeMock.mockImplementation((module: string, action: string) => {
      if (module === 'targets' && action === 'list') return Promise.resolve({ targets: [] });
      if (module === 'employeelifecycle' && action === 'getHygiene') return Promise.resolve(environment);
      if (module === 'inventory' && action === 'listHosts') return Promise.resolve({ hosts: [{ host: 'SCAN-ONLY', capturedAtUtc: '2026-08-17T06:00:00Z' }] });
      return Promise.reject(new Error(`Unexpected action ${module}/${action}`));
    });
  });

  it('shows the compact shared inventory including disabled and unmanaged devices', async () => {
    renderPage();

    expect(await screen.findByText('DISABLED-PC')).toBeTruthy();
    expect(screen.getByText('PC01')).toBeTruthy();
    expect(screen.getByText('SCAN-ONLY')).toBeTruthy();
    for (const heading of ['Device', 'AD', 'Kaspersky', 'opsi', 'Nessus', 'Overall']) {
      expect(screen.getByRole('columnheader', { name: heading })).toBeTruthy();
    }
    expect(screen.getByText('Disabled')).toBeTruthy();
    expect(screen.getAllByText('Unmanaged')).toHaveLength(2);
  });

  it('filters by source and opens the common detail route', async () => {
    renderPage();
    await screen.findByText('PC01');

    await userEvent.selectOptions(screen.getByLabelText('Filter clients by source'), 'OPSI');
    expect(screen.queryByText('DISABLED-PC')).toBeNull();
    expect(screen.queryByText('SCAN-ONLY')).toBeNull();
    await userEvent.click(screen.getByText('PC01'));
    expect(await screen.findByText('Shared client detail')).toBeTruthy();
  });
});
