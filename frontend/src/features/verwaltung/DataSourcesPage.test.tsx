import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { DataSourcesPage } from './DataSourcesPage';

const mocks = vi.hoisted(() => ({ invoke: vi.fn(), refresh: vi.fn(), clear: vi.fn(), invalidate: vi.fn(), ksc: vi.fn() }));
vi.mock('../../shared/bridge/bridgeClient', () => ({ invoke: mocks.invoke }));
vi.mock('../../shared/bridge/errorPresentation', () => ({ presentError: () => ({ message: 'Source status unavailable.' }) }));
vi.mock('../../shared/environment/EnvironmentContext', () => ({ useEnvironment: () => ({ result: null, loading: false, error: null, refresh: mocks.refresh, invalidate: mocks.invalidate }) }));
vi.mock('../../shared/objects/WorkingSetContext', () => ({ useOptionalWorkingSet: () => ({ clearFamily: mocks.clear, refreshCached: vi.fn() }) }));
vi.mock('../../shared/targets/TargetContext', () => ({ useTargets: () => ({ kasperskyCredentials: null, signInKaspersky: mocks.ksc, signOutKaspersky: vi.fn() }) }));
describe('data source sessions', () => {
  beforeEach(() => {
    mocks.invoke.mockReset(); mocks.clear.mockClear(); mocks.refresh.mockClear();
    mocks.invoke.mockImplementation((_module, action) => {
      if (action === 'getOpsiSettings') return Promise.resolve({ settings: { server: 'opsi.example', trustServerCertificate: false } });
      if (action === 'getConnectionStatus') return Promise.resolve({ connected: false, connectionError: null });
      if (action === 'getItLifecycleSettings') return Promise.reject(new Error('provider details'));
      if (action === 'getCredentialStatus') return Promise.resolve({ saved: true });
      if (action === 'getServiceCredentialStatuses') return Promise.resolve({ kaspersky: { saved: false }, opsi: { saved: true } });
      return Promise.resolve({ connected: true, serverUrl: 'opsi.example', userName: 'stored-reader' });
    });
  });
  it('retains independent status failures and connects only after the explicit action', async () => {
    render(<MemoryRouter><DataSourcesPage /></MemoryRouter>);
    expect(await screen.findByText(/Kaspersky configuration: Source status unavailable/)).toBeTruthy();
    expect(screen.getByText(/API keys stored securely/)).toBeTruthy();
    expect(mocks.refresh).not.toHaveBeenCalled();
    expect(mocks.invoke).toHaveBeenCalledWith('patchmanagement', 'getConnectionStatus', { connectStoredCredential: false });
    expect(mocks.invoke.mock.calls.some(([, action]) => action === 'connect')).toBe(false);
    fireEvent.click(screen.getByRole('button', { name: 'Connect saved opsi account' }));
    await waitFor(() => expect(mocks.invoke).toHaveBeenCalledWith('patchmanagement', 'connect', {
      server: 'opsi.example', trustServerCertificate: false, userName: '', password: null, useStoredCredential: true, rememberCredential: false,
    }));
    expect(mocks.clear).toHaveBeenCalledWith('management');
    expect(mocks.invoke.mock.calls.some(([, action]) => action.startsWith('save'))).toBe(false);
  });
});
