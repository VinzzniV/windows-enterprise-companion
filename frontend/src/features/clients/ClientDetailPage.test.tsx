import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { TargetProvider } from '../../shared/targets/TargetContext';
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
        <Routes>
          <Route path="/clients/:host" element={<ClientDetailPage />} />
        </Routes>
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
