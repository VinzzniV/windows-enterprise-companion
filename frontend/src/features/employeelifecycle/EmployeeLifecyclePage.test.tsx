import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import type { ItHygieneResult } from '../../shared/api-types';
import { TargetProvider, useTargets } from '../../shared/targets/TargetContext';
import { EnvironmentProvider } from '../../shared/environment/EnvironmentContext';
import { EmployeeLifecyclePage } from './EmployeeLifecyclePage';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../shared/bridge/bridgeClient', () => ({
  invoke: invokeMock,
  BridgeInvokeError: class extends Error {},
}));

const result: ItHygieneResult = {
  assessedAtUtc: '2026-08-17T12:00:00Z',
  domainName: 'example.test',
  sources: {
    activeDirectory: { availability: 'AVAILABLE', error: null },
    kaspersky: { availability: 'AVAILABLE', error: null },
    opsi: { availability: 'AVAILABLE', error: null },
    nessus: { availability: 'AVAILABLE', error: null },
  },
  summary: {
    total: 2,
    adComputers: 2,
    kasperskyComputers: 1,
    opsiComputers: 0,
    nessusComputers: 1,
    healthy: 1,
    problems: 1,
    incomplete: 0,
    stale: 0,
    missingKaspersky: 1,
    orphanKaspersky: 0,
    missingOpsi: 0,
    orphanOpsi: 0,
    outdated: 0,
    missingNessus: 0,
    staleNessus: 0,
    nessusCritical: 0,
    nessusHigh: 0,
  },
  devices: [
    {
      computerName: 'PC001',
      hostName: 'PC001.example.test',
      activeDirectory: {
        exists: true,
        enabled: true,
        dnsHostName: 'PC001.example.test',
        operatingSystem: 'Windows 11 Pro',
        description: 'Test client',
        distinguishedName: 'CN=PC001,OU=Clients,DC=example,DC=test',
        organizationalUnit: 'OU=Clients,DC=example,DC=test',
        lastLogonDate: '2026-08-15T12:00:00Z',
      },
      kaspersky: {
        exists: true,
        lastSeen: '2026-08-16T12:00:00Z',
        agentVersion: '16.0.0.254',
        kesVersion: '21.25.7.504',
        administrationGroup: 'Workstations',
      },
      opsi: { exists: false, clientId: null, description: null, depotId: null, lastSeen: null, clientAgentVersion: null },
      nessus: { exists: true, assetId: '1', ipAddress: '10.0.0.1', lastCompletedScanUtc: '2026-08-16T12:00:00Z', critical: 0, high: 0, medium: 0, low: 0, info: 0, ports: [], scanSources: ['Clients'] },
      assessment: { status: 'HEALTHY', findings: [] },
    },
    {
      computerName: 'PC002',
      hostName: 'PC002.example.test',
      activeDirectory: {
        exists: true,
        enabled: true,
        dnsHostName: 'PC002.example.test',
        operatingSystem: 'Windows 11 Pro',
        description: 'Test client',
        distinguishedName: 'CN=PC002,OU=Clients,DC=example,DC=test',
        organizationalUnit: 'OU=Clients,DC=example,DC=test',
        lastLogonDate: '2026-08-13T12:00:00Z',
      },
      kaspersky: {
        exists: false,
        lastSeen: null,
        agentVersion: null,
        kesVersion: null,
        administrationGroup: null,
      },
      opsi: { exists: false, clientId: null, description: null, depotId: null, lastSeen: null, clientAgentVersion: null },
      nessus: { exists: false, assetId: null, ipAddress: null, lastCompletedScanUtc: null, critical: 0, high: 0, medium: 0, low: 0, info: 0, ports: [], scanSources: [] },
      assessment: {
        status: 'WARNING',
        findings: [
          {
            code: 'MISSING_KASPERSKY',
            severity: 'WARNING',
            message: 'Enabled in Active Directory, but no matching Kaspersky device was found.',
          },
        ],
      },
    },
  ],
};

beforeEach(() => {
  invokeMock.mockReset();
  invokeMock.mockImplementation((module, action) => {
    if (module === 'targets' && action === 'list') return Promise.resolve({ targets: [] });
    return Promise.resolve(result);
  });
});

function renderPage() {
  return render(
    <MemoryRouter>
      <TargetProvider>
        <EnvironmentProvider>
          <Routes>
            <Route path="*" element={<EmployeeLifecyclePage />} />
            <Route path="/clients/:host" element={<div>Client overview route</div>} />
          </Routes>
        </EnvironmentProvider>
      </TargetProvider>
    </MemoryRouter>,
  );
}

describe('EmployeeLifecyclePage IT hygiene MVP', () => {
  it('shows summary and the correlated device table', async () => {
    renderPage();

    expect(await screen.findByText('PC001')).toBeTruthy();
    expect(screen.getByText('PC002')).toBeTruthy();
    expect(screen.getByText('Devices total').parentElement?.textContent).toContain('2');
    expect(screen.getAllByText('Missing Kaspersky')[0].parentElement?.textContent).toContain('1');
    expect(invokeMock).toHaveBeenCalledWith(
      'employeelifecycle',
      'getHygiene',
      expect.any(Object),
      180_000,
    );
  });

  it('filters and searches devices', async () => {
    renderPage();
    await screen.findByText('PC001');

    await userEvent.selectOptions(screen.getByLabelText('Filter devices'), 'MISSING_KASPERSKY');
    expect(screen.queryByText('PC001')).toBeNull();
    expect(screen.getByText('PC002')).toBeTruthy();

    await userEvent.selectOptions(screen.getByLabelText('Filter devices'), 'ALL');
    await userEvent.type(screen.getByLabelText('Search devices'), 'workstations');
    expect(screen.getByText('PC001')).toBeTruthy();
    expect(screen.queryByText('PC002')).toBeNull();
  });

  it('opens the shared client detail route after selecting a device', async () => {
    renderPage();
    await userEvent.click(await screen.findByText('PC002'));

    expect(await screen.findByText('Client overview route')).toBeTruthy();
  });

  it('never labels a stale opsi status as OK', async () => {
    const staleResult: ItHygieneResult = {
      ...result,
      devices: [{
        ...result.devices[0],
        computerName: 'STALE-OPSI',
        opsi: {
          exists: true,
          clientId: 'stale-opsi.example.test',
          description: null,
          depotId: 'depot.example.test',
          lastSeen: '2026-04-01T12:00:00Z',
          clientAgentVersion: '4.3.8',
        },
        assessment: {
          status: 'CLEANUP_CANDIDATE',
          findings: [{
            code: 'STALE_OPSI',
            severity: 'CRITICAL',
            message: 'opsi last seen was more than 90 days ago.',
          }],
        },
      }],
    };
    invokeMock.mockImplementation((module, action) => {
      if (module === 'targets' && action === 'list') return Promise.resolve({ targets: [] });
      return Promise.resolve(staleResult);
    });

    renderPage();

    const row = (await screen.findByText('STALE-OPSI')).closest('tr');
    expect(row).not.toBeNull();
    const opsiCell = row!.querySelectorAll('td')[3];
    expect(opsiCell.textContent).toBe('Stale');
    expect(opsiCell.querySelector('span')?.className).toContain('border-fail-700');
  });

  it('does not send a domainless KSC account to Active Directory', async () => {
    invokeMock.mockImplementation((module, action) => {
      if (module === 'targets' && action === 'list') return Promise.resolve({ targets: [] });
      return Promise.resolve(result);
    });

    function SignInAndPage() {
      const targets = useTargets();
      return (
        <>
          <button
            onClick={() => targets.signInKaspersky({ userName: 'ksc-reader', domain: '', password: 'secret' })}
          >
            Sign in KSC
          </button>
          <EmployeeLifecyclePage />
        </>
      );
    }

    render(
      <MemoryRouter>
        <TargetProvider>
          <EnvironmentProvider>
            <SignInAndPage />
          </EnvironmentProvider>
        </TargetProvider>
      </MemoryRouter>,
    );
    await userEvent.click(screen.getByRole('button', { name: 'Sign in KSC' }));
    await screen.findByText('PC001');

    const hygieneCall = invokeMock.mock.calls
      .filter((call) => call[1] === 'getHygiene')
      .at(-1);
    expect(hygieneCall?.[2]).toEqual({
      activeDirectory: {},
      kaspersky: { userName: 'ksc-reader', domain: null, password: 'secret' },
    });
  });
});
