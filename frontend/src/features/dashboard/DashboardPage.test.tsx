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
          },
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
    expect(screen.getByText('Inventory')).toBeDefined();
    expect(screen.getByText('Active Directory')).toBeDefined();
    // Derived metrics from stored data
    expect(await screen.findByText('1 host')).toBeDefined();
    expect(await screen.findByText('1 critical/high')).toBeDefined();
    expect(await screen.findByText('Not connected')).toBeDefined();

    const inventoryLink = screen.getByText('Inventory').closest('a');
    expect(inventoryLink?.getAttribute('href')).toBe('/inventory');
  });

  it('degrades to empty tiles when the bridge is unavailable', async () => {
    invokeMock.mockRejectedValue(new Error('no bridge'));

    render(
      <MemoryRouter>
        <DashboardPage />
      </MemoryRouter>,
    );

    expect(await screen.findByText('No hosts')).toBeDefined();
    expect(await screen.findByText('No scan')).toBeDefined();
  });
});
