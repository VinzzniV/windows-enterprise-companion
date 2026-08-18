import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { TargetProvider } from '../../shared/targets/TargetContext';
import { EnvironmentProvider } from '../../shared/environment/EnvironmentContext';
import { ClientDetailPage } from './ClientDetailPage';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../shared/bridge/bridgeClient', () => ({
  invoke: invokeMock,
  BridgeInvokeError: class extends Error {},
  subscribe: () => () => {},
}));

function renderAt(host: string) {
  return render(
    <MemoryRouter initialEntries={[`/clients/${encodeURIComponent(host)}`]}>
      <TargetProvider>
        <EnvironmentProvider>
          <Routes>
            <Route path="/clients/:host" element={<ClientDetailPage />} />
          </Routes>
        </EnvironmentProvider>
      </TargetProvider>
    </MemoryRouter>,
  );
}

describe('ClientDetailPage', () => {
  beforeEach(() => {
    invokeMock.mockReset();
    invokeMock.mockImplementation((module: string, action: string) => {
      if (module === 'system' && action === 'getAppInfo') {
        return Promise.resolve({
          version: 'x',
          databasePath: '',
          logDirectory: '',
          isElevated: false,
          maxParallelScans: 4,
          machineName: 'WEC-HOST',
        });
      }
      if (module === 'inventory' && action === 'getHardwareInfo') {
        return Promise.reject(new Error('no cached snapshot'));
      }
      if (module === 'employeelifecycle' && action === 'getHygiene') {
        return Promise.resolve({
          assessedAtUtc: '2026-08-18T08:00:00Z', domainName: 'corp.local',
          sources: {
            activeDirectory: { availability: 'AVAILABLE', error: null },
            kaspersky: { availability: 'AVAILABLE', error: null },
            opsi: { availability: 'UNAVAILABLE', error: 'opsi service unavailable' },
          },
          summary: { total: 1, adComputers: 1, kasperskyComputers: 0, opsiComputers: 0, healthy: 0, problems: 1, incomplete: 0, stale: 0, missingKaspersky: 1, orphanKaspersky: 0, missingOpsi: 0, orphanOpsi: 0, outdated: 0 },
          devices: [{
            computerName: 'PC1', hostName: 'PC1.corp.local',
            activeDirectory: { exists: true, enabled: true, dnsHostName: 'PC1.corp.local', operatingSystem: 'Windows 11 Pro', description: 'Test notebook', distinguishedName: 'CN=PC1,OU=Clients,DC=corp,DC=local', organizationalUnit: 'OU=Clients,DC=corp,DC=local', lastLogonDate: '2026-08-17T08:00:00Z' },
            kaspersky: { exists: false, lastSeen: null, agentVersion: null, kesVersion: null, administrationGroup: null },
            opsi: { exists: false, clientId: null, description: null, depotId: null, lastSeen: null, clientAgentVersion: null },
            assessment: { status: 'WARNING', findings: [{ code: 'MISSING_KASPERSKY', severity: 'WARNING', message: 'Enabled in Active Directory, but no matching Kaspersky device was found.' }] },
          }],
        });
      }
      if (module === 'security' && action === 'getLatestScan') return Promise.resolve({ scan: null });
      if (module === 'reporting' && action === 'getOverview') {
        return Promise.resolve({
          inventoryCapturedAtUtc: null,
          securityScanCompletedAtUtc: null,
          securityScanStatus: null,
          securityFindingCount: null,
        });
      }
      return Promise.resolve({ targets: [] });
    });
  });

  it('shows a credential bar for a remote client and runs each section on demand', async () => {
    renderAt('PC1.corp.local');

    expect(await screen.findByText('Scanning as current user')).toBeDefined(); // remote → creds needed
    expect((await screen.findByRole('tab', { name: 'Overview' })).getAttribute('aria-selected')).toBe('true');
    expect(await screen.findByText('Environment assessment')).toBeDefined();
    expect(screen.getByText('MISSING KASPERSKY').parentElement?.textContent)
      .toContain('Enabled in Active Directory, but no matching Kaspersky device was found.');
    expect(screen.getByText('Windows 11 Pro')).toBeDefined();
    expect(screen.getByText('opsi service unavailable')).toBeDefined();
    fireEvent.click(screen.getByRole('tab', { name: 'Inventory' }));
    expect(await screen.findByText('Run inventory scan')).toBeDefined(); // no cache → on-demand

    fireEvent.click(screen.getByRole('tab', { name: 'Security' }));
    expect(await screen.findByText('Run security scan')).toBeDefined();

    fireEvent.click(screen.getByRole('tab', { name: 'Diagnostics' }));
    expect(await screen.findByText('Run diagnostics')).toBeDefined();

    fireEvent.click(screen.getByRole('tab', { name: 'Reporting' }));
    // Remote reporting now works: the section reports on the scanned host by name
    expect(await screen.findByText('Included data — PC1.corp.local')).toBeDefined();
    expect(await screen.findByRole('button', { name: 'Export HTML' })).toBeDefined();
  });
});
