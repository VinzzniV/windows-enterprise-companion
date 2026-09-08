import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { DashboardPage } from './DashboardPage';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../shared/bridge/bridgeClient', () => ({
  invoke: invokeMock,
  BridgeInvokeError: class extends Error {},
}));

describe('DashboardPage', () => {
  beforeEach(() => {
    invokeMock.mockReset();
  });

  it('renders module tiles from the stored data of each module', async () => {
    invokeMock.mockImplementation((module: string, action: string) => {
      if (module === 'inventory' && action === 'listHosts') {
        return Promise.resolve({
          hosts: [{ host: 'PC1', capturedAtUtc: '2026-07-03T08:00:00Z' }],
        });
      }
      if (module === 'security' && action === 'getLatestScan') {
        return Promise.resolve({
          scan: {
            scanId: 1,
            host: 'PC1',
            startedAtUtc: '2026-07-03T08:00:00Z',
            completedAtUtc: '2026-07-03T08:01:00Z',
            status: 'COMPLETED',
            findings: [
              { findingId: 'A', severity: 'CRITICAL', title: 't', description: 'd', category: 'SYSTEM', affectedResource: 'r', recommendation: 'x', evidence: {}, requiredPrivilege: null },
            ],
            checkResults: [],
            coverageVersion: 1,
            coverage: {
              isKnown: true,
              totalChecks: 13,
              succeededChecks: 13,
              failedChecks: 0,
              requiresElevationChecks: 0,
              notApplicableChecks: 0,
              applicableChecks: 13,
              isComplete: true,
            },
          },
        });
      }
      if (module === 'reporting' && action === 'getReadinessPolicy') {
        return Promise.resolve({
          maximumInventoryAgeSeconds: 86_400,
          maximumSecurityScanAgeSeconds: 86_400,
        });
      }
      if (module === 'printmanagement' && action === 'listServers') {
        return Promise.resolve({ servers: [] });
      }
      if (module === 'patchmanagement' && action === 'getConnectionStatus') {
        return Promise.resolve({
          connected: false,
          serverUrl: null,
          userName: null,
          opsiVersion: null,
          defaultDepotFilter: 'Denkingen',
        });
      }
      return Promise.reject(new Error(`unexpected ${module}/${action}`));
    });

    render(
      <MemoryRouter>
        <DashboardPage />
      </MemoryRouter>,
    );

    // Tiles for every module are present with links into them
    expect(screen.getByText('Clients')).toBeDefined();
    expect(screen.getByText('Active Directory')).toBeDefined();
    expect(screen.getByText('Report export')).toBeDefined();
    // Derived metrics from stored data
    expect(await screen.findByText('1 stored host')).toBeDefined();
    expect(await screen.findByText('1 critical/high')).toBeDefined();
    expect(await screen.findByText('Not connected')).toBeDefined();

    const clientsLink = screen.getByText('Clients').closest('a');
    expect(clientsLink?.getAttribute('href')).toBe('/clients');
  });

  it('degrades to empty tiles when the bridge is unavailable', async () => {
    invokeMock.mockRejectedValue(new Error('no bridge'));

    render(
      <MemoryRouter>
        <DashboardPage />
      </MemoryRouter>,
    );

    expect((await screen.findAllByText('Unavailable')).length).toBeGreaterThanOrEqual(5);
    expect(screen.queryByText('No hosts')).toBeNull();
    expect(screen.queryByText('No scan')).toBeNull();
    expect(screen.getAllByText('Failed').length).toBeGreaterThanOrEqual(5);
  });

  it('shows explicit loading states instead of empty or healthy values while sources resolve', () => {
    invokeMock.mockImplementation(() => new Promise(() => {}));

    render(
      <MemoryRouter>
        <DashboardPage />
      </MemoryRouter>,
    );

    expect(screen.getAllByText('Running').length).toBeGreaterThanOrEqual(5);
    expect(screen.queryByText('No hosts')).toBeNull();
    expect(screen.queryByText('No scan')).toBeNull();
  });
});
