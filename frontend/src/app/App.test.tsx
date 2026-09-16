import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { App, AppInfoFooter, type AppInfoState } from './App';
import type { ReportOverview } from '../shared/api-types';

vi.mock('../features/dashboard/DashboardPage', () => ({
  DashboardPage: () => <div>Dashboard content</div>,
}));

vi.mock('../features/clients/ClientsPage', () => ({
  ClientsPage: () => <div>Clients content</div>,
}));
vi.mock('../shared/objects/ObjectWorkingSetPage', () => ({
  DevicesWorkingSetPage: () => <div>Device objects content</div>,
  UsersWorkingSetPage: () => <div>User objects content</div>,
  GroupsWorkingSetPage: () => <div>Group objects content</div>,
}));

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../shared/bridge/bridgeClient', () => ({
  invoke: invokeMock,
  invokeCancellable: (module: string, action: string, payload: unknown) => ({
    requestId: 'request-id',
    promise: action === 'getStoredObjectLists' ? Promise.resolve({ workspace: { scope: 'local', localComputerName: 'TESTHOST' },
      maximumRecords: 5000, maximumSourceReads: 128, retrievedAtUtc: '2026-09-14T10:00:00Z', search: null, reads: [] })
      : action === 'getCachedObjectLists' ? Promise.resolve({ tenantId: null, sessionRevision: 0, revision: 0,
        recordLimit: 5000, cachedSourceRecords: 0, loadedSourceRecords: 0, truncated: false, reads: [] })
        : action === 'getCachedManagementObjectLists' ? Promise.resolve({ workspace: { scope: 'local', localComputerName: 'TESTHOST' },
          snapshotId: null, opsiSessionId: null, sessionRevision: 0, revision: 0, retrievedAtUtc: null, maximumRecords: 5000, search: null, reads: [] }) : invokeMock(module, action, payload),
    cancel: vi.fn(),
  }),
  subscribe: vi.fn(() => () => {}),
  BridgeInvokeError: class extends Error {},
  BridgeCancelledError: class extends Error {},
  BridgeTimeoutError: class extends Error {},
  BridgeUnavailableError: class extends Error {},
}));

const loadedState: AppInfoState = {
  kind: 'loaded',
  appInfo: {
    version: '1.2.3',
    databasePath: 'C:\\private\\wec.db',
    logDirectory: 'C:\\private\\logs',
    isElevated: false,
    maxParallelScans: 4,
    maxBatchHosts: 50,
    machineName: 'TESTHOST',
    runtimeProfile: 'Installed',
  },
};

describe('AppInfoFooter', () => {
  beforeEach(() => invokeMock.mockReset());

  it('shows an explicit loading state', () => {
    render(<AppInfoFooter state={{ kind: 'loading' }} />);

    expect(screen.getByText('Loading application information…')).toBeDefined();
  });

  it('shows a failed app-info request instead of silently hiding the footer', () => {
    render(<AppInfoFooter state={{
      kind: 'error',
      error: {
        message: 'Application information could not be loaded.',
        cause: 'The desktop host is unavailable.',
        action: 'Restart the application.',
        technicalDetails: 'raw bridge unavailable failure',
      },
    }} />);

    const alert = screen.getByRole('alert');
    expect(within(alert).getByText('Application information could not be loaded.')).toBeDefined();
    expect(within(alert).getByText('Cause')).toBeDefined();
    expect(within(alert).getByText('Next action')).toBeDefined();
    const details = within(alert).getByText('Technical details').closest('details') as HTMLDetailsElement;
    expect(details.open).toBe(false);
  });

  it('shows runtime identity and a full-size log action without exposing local paths', () => {
    render(<AppInfoFooter state={loadedState} />);

    expect(screen.getByText('v1.2.3')).toBeDefined();
    expect(screen.getByText('Profile: Installed')).toBeDefined();
    expect(screen.getByRole('button', { name: 'Open log folder' })).toBeDefined();
    expect(screen.queryByText(/C:\\private/i)).toBeNull();
  });

  it('surfaces a failure to open the log folder', async () => {
    invokeMock.mockReturnValue({
      catch: (onRejected: (error: Error) => void) => {
        onRejected(new Error('raw footer folder failure'));
        return Promise.resolve();
      },
    });
    render(<AppInfoFooter state={loadedState} />);
    expect(invokeMock).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole('button', { name: 'Open log folder' }));

    const alert = await screen.findByRole('alert');
    expect(within(alert).getByText('The log folder could not be opened.')).toBeDefined();
    expect(within(alert).getByText('Next action')).toBeDefined();
    const details = within(alert).getByText('Technical details').closest('details') as HTMLDetailsElement;
    expect(details.open).toBe(false);
  });
});

describe('responsive application shell', () => {
  beforeEach(() => {
    window.location.hash = '#/';
    invokeMock.mockReset();
    invokeMock.mockImplementation((module: string, action: string) => {
      if (module === 'system' && action === 'getAppInfo') return Promise.resolve(loadedState.appInfo);
      if (module === 'targets' && action === 'list') return Promise.resolve({ targets: [] });
      if (module === 'inventory' && action === 'listHosts') return Promise.resolve({ hosts: [] });
      if (module === 'security' && action === 'listHosts') return Promise.resolve({ hosts: [] });
      return Promise.resolve({});
    });
  });

  it('offers an accessible mobile drawer without duplicating the closed navigation', async () => {
    render(<App />);

    expect(screen.getByTestId('desktop-navigation').className).toContain('hidden');
    expect(screen.queryByRole('dialog', { name: 'Main navigation' })).toBeNull();

    const openNavigation = screen.getByRole('button', { name: 'Open navigation' });
    fireEvent.click(openNavigation);

    const drawer = screen.getByRole('dialog', { name: 'Main navigation' });
    expect(drawer.className).toContain('xl:hidden');
    expect(screen.getByTestId('application-main').hasAttribute('inert')).toBe(true);
    expect(within(drawer).getByRole('link', { name: 'Error log' })).toBeDefined();
    expect(within(drawer).getByRole('link', { name: 'Network Scan' })).toBeDefined();
    expect(within(drawer).getByText('Administration')).toBeDefined();
    expect(within(drawer).queryByText('Netzwerkscan')).toBeNull();
    expect(within(drawer).queryByText('Verwaltung')).toBeNull();
    expect(within(drawer).queryByRole('link', { name: 'IT Lifecycle' })).toBeNull();

    fireEvent.keyDown(window, { key: 'Escape' });
    expect(screen.queryByRole('dialog', { name: 'Main navigation' })).toBeNull();
    expect(screen.getByTestId('application-main').hasAttribute('inert')).toBe(false);
    expect(document.activeElement).toBe(openNavigation);
  });

  it('navigates between lazy workspaces through the primary navigation', async () => {
    render(<App />);

    expect(await screen.findByText('Dashboard content')).toBeDefined();
    fireEvent.click(within(screen.getByTestId('desktop-navigation')).getByRole('link', { name: 'Devices' }));

    expect(await screen.findByText('Device objects content')).toBeDefined();
    expect(window.location.hash).toBe('#/devices');
  });

  it('opens global search with Ctrl+K and restores focus after Escape', async () => {
    render(<App />);
    await screen.findByText('Dashboard content');
    const opener = screen.getByRole('button', { name: 'Open global search' });

    fireEvent.keyDown(window, { key: 'k', ctrlKey: true });

    const dialog = screen.getByRole('dialog', { name: 'Global search' });
    const input = within(dialog).getByRole('combobox');
    await waitFor(() => expect(document.activeElement).toBe(input));
    expect(screen.getByTestId('application-main').hasAttribute('inert')).toBe(true);
    fireEvent.keyDown(input, { key: 'Escape' });

    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Global search' })).toBeNull());
    await waitFor(() => expect(document.activeElement).toBe(opener));
    expect(screen.getByTestId('application-main').hasAttribute('inert')).toBe(false);
  });

  it('keeps lifecycle bookmarks working by redirecting their filter to Clients posture', async () => {
    window.location.hash = '#/employeelifecycle?filter=OUTDATED';

    render(<App />);

    expect(await screen.findByText('Clients content')).toBeDefined();
    await waitFor(() => expect(window.location.hash).toBe('#/clients?posture=OUTDATED'));
  });

  it('uses the reported subject identity for global report readiness links without another app-info request', async () => {
    window.location.hash = '#/reporting';
    const missingOverview = {
      subjectHost: 'TESTHOST',
      inventoryCapturedAtUtc: null,
      securityScanCompletedAtUtc: null,
      securityScanStatus: null,
      securityFindingCount: null,
      securityCoverage: null,
      readiness: {
        evaluatedAtUtc: '2026-08-19T16:00:00Z',
        isReady: false,
        sources: [
          {
            source: 'Hardware inventory',
            provenance: 'Persisted WMI/CIM inventory snapshot',
            state: 'MISSING',
            capturedAtUtc: null,
            ageSeconds: null,
            isComplete: false,
            summary: 'No hardware inventory data is available.',
          },
          {
            source: 'Security posture',
            provenance: 'Persisted Security scan and per-check outcomes',
            state: 'MISSING',
            capturedAtUtc: null,
            ageSeconds: null,
            isComplete: false,
            summary: 'No security posture data is available.',
          },
        ],
      },
    } satisfies ReportOverview;
    invokeMock.mockImplementation((module: string, action: string) => {
      if (module === 'system' && action === 'getAppInfo') return Promise.resolve(loadedState.appInfo);
      if (module === 'targets' && action === 'list') return Promise.resolve({ targets: [] });
      if (module === 'reporting' && action === 'getOverview') return Promise.resolve(missingOverview);
      return Promise.resolve({});
    });

    render(<App />);

    expect((await screen.findByRole('link', { name: 'Open Inventory' })).getAttribute('href'))
      .toBe('#/clients/TESTHOST?section=inventory');
    expect(screen.getByRole('link', { name: 'Open Security' }).getAttribute('href'))
      .toBe('#/clients/TESTHOST?section=security');
    expect(invokeMock.mock.calls.filter((call) => call[0] === 'system' && call[1] === 'getAppInfo'))
      .toHaveLength(1);
  });

  it('keeps an elevation failure actionable and its diagnostics collapsed', async () => {
    invokeMock.mockImplementation((module: string, action: string) => {
      if (module === 'system' && action === 'getAppInfo') return Promise.resolve(loadedState.appInfo);
      if (module === 'targets' && action === 'list') return Promise.resolve({ targets: [] });
      if (module === 'system' && action === 'restartElevated') {
        return Promise.reject(new Error('raw elevation launch failure'));
      }
      return Promise.resolve({});
    });

    render(<App />);
    fireEvent.click(await screen.findByRole('button', { name: /Restart as administrator/ }));

    const alert = await screen.findByRole('alert');
    expect(within(alert).getByText('The elevated application could not be started.')).toBeDefined();
    expect(within(alert).getByText('Next action')).toBeDefined();
    const details = within(alert).getByText('Technical details').closest('details') as HTMLDetailsElement;
    expect(details.open).toBe(false);
  });
});
