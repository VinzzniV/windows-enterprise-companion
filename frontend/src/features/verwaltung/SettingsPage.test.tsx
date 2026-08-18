import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { SettingsPage } from './SettingsPage';
import { TargetProvider } from '../../shared/targets/TargetContext';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../shared/bridge/bridgeClient', () => ({
  invoke: invokeMock,
  BridgeInvokeError: class extends Error {},
}));

const settings = {
  inventoryLimit: 10000,
  staleWarningDays: 60,
  staleCriticalDays: 90,
  targetAgentVersion: '',
  targetKesVersion: '',
  kaspersky: {
    server: '',
    port: 13299,
    requestTimeoutSeconds: 60,
    trustedCertificateThumbprint: '',
    excludedAdministrationGroups: ['Nicht für Kaspersky geeignete Geräte'],
  },
};

const opsiSettings = {
  server: 'opsi.example.test',
  port: 4447,
  requestTimeoutSeconds: 90,
  defaultDepotFilter: 'depot01',
  trustServerCertificate: true,
};

beforeEach(() => {
  invokeMock.mockReset();
  invokeMock.mockImplementation((_module: string, action: string) => {
    if (action === 'getAppInfo') {
      return Promise.resolve({
        version: '0.2.0',
        databasePath: 'wec.db',
        logDirectory: 'logs',
        isElevated: false,
        maxParallelScans: 4,
        machineName: 'TEST-PC',
      });
    }
    if (action === 'getItLifecycleSettings') {
      return Promise.resolve({ settings, restartRequired: false });
    }
    if (action === 'saveItLifecycleSettings') {
      return Promise.resolve({ settings: { ...settings, kaspersky: { ...settings.kaspersky, server: 'ksc.local' } }, restartRequired: true });
    }
    if (action === 'getOpsiSettings') {
      return Promise.resolve({ settings: opsiSettings, restartRequired: false });
    }
    if (action === 'saveOpsiSettings') {
      return Promise.resolve({ settings: opsiSettings, restartRequired: true });
    }
    if (action === 'getConnectionStatus') {
      return Promise.resolve({ connected: false, serverUrl: null, userName: null, opsiVersion: null, defaultDepotFilter: '' });
    }
    if (action === 'getServiceCredentialStatuses') {
      return Promise.resolve({
        kaspersky: { saved: false, userName: null, domain: null },
        opsi: { saved: false, userName: null, domain: null },
      });
    }
    if (action === 'saveServiceCredential') {
      return Promise.resolve({ saved: true, userName: 'ksc-reader', domain: null });
    }
    if (action === 'connect') {
      return Promise.resolve({ connected: true, serverUrl: 'https://opsi.example.test:4447/rpc', userName: 'opsi-reader', opsiVersion: '4.3', defaultDepotFilter: 'depot01' });
    }
    if (action === 'list') return Promise.resolve({ targets: [] });
    return Promise.resolve({});
  });
});

function renderSettings() {
  return render(
    <TargetProvider>
      <SettingsPage />
    </TargetProvider>,
  );
}

describe('SettingsPage', () => {
  it('loads and saves IT Lifecycle settings in-app', async () => {
    renderSettings();

    const server = await screen.findByLabelText('Kaspersky Administration Server');
    await userEvent.type(server, 'ksc.local');
    await userEvent.click(screen.getByRole('button', { name: 'Save IT Lifecycle settings' }));

    expect(invokeMock).toHaveBeenCalledWith(
      'system',
      'saveItLifecycleSettings',
      expect.objectContaining({
        settings: expect.objectContaining({
          kaspersky: expect.objectContaining({ server: 'ksc.local' }),
        }),
      }),
    );
    expect(await screen.findByText('Saved. Restart WEC to apply these settings.')).toBeTruthy();
  });

  it('keeps the KSC password out of persisted settings', async () => {
    renderSettings();

    expect(await screen.findByText(/Future modules should add their settings here/)).toBeTruthy();
    await userEvent.type(screen.getByLabelText('User name'), 'ksc-reader');
    await userEvent.type(screen.getByLabelText('Password'), 'secret');
    await userEvent.click(screen.getByRole('button', { name: 'Save IT Lifecycle settings' }));

    const savePayload = invokeMock.mock.calls.find((call) => call[1] === 'saveItLifecycleSettings')?.[2];
    expect(JSON.stringify(savePayload)).not.toContain('secret');
  });

  it('stores and signs in a dedicated KSC account', async () => {
    renderSettings();

    await screen.findByLabelText('User name');
    await userEvent.type(screen.getByLabelText('User name'), 'ksc-reader');
    await userEvent.type(screen.getByLabelText('Password'), 'secret');
    await userEvent.click(screen.getByRole('button', { name: 'Save securely and sign in KSC' }));

    expect(screen.getAllByText('ksc-reader')).toHaveLength(2);
    expect(screen.getByRole('button', { name: 'Sign out KSC' })).toBeTruthy();
    expect(invokeMock).toHaveBeenCalledWith('system', 'saveServiceCredential', {
      kind: 'KASPERSKY', userName: 'ksc-reader', domain: null, password: 'secret',
    });
  });

  it('persists opsi connection settings separately and requests secure credential storage', async () => {
    renderSettings();

    await screen.findByLabelText('opsi server');
    await userEvent.click(screen.getByRole('button', { name: 'Save opsi settings' }));
    await userEvent.type(screen.getByLabelText('opsi user name'), 'opsi-reader');
    await userEvent.type(screen.getByLabelText('opsi password'), 'session-secret');
    await userEvent.click(screen.getByRole('button', { name: 'Connect opsi' }));

    const savePayload = invokeMock.mock.calls.find((call) => call[1] === 'saveOpsiSettings')?.[2];
    expect(savePayload).toEqual({ settings: opsiSettings });
    expect(JSON.stringify(savePayload)).not.toContain('session-secret');
    const connectPayload = invokeMock.mock.calls.find((call) => call[1] === 'connect')?.[2];
    expect(connectPayload).toEqual({
      server: 'opsi.example.test',
      userName: 'opsi-reader',
      password: 'session-secret',
      trustServerCertificate: true,
      rememberCredential: true,
    });
    expect(screen.queryByLabelText('opsi password')).toBeNull();
  });
});
