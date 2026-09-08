import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { SettingsPage } from './SettingsPage';
import { TargetProvider } from '../../shared/targets/TargetContext';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../shared/bridge/bridgeClient', () => ({
  invoke: invokeMock,
  BridgeInvokeError: class extends Error {},
  BridgeCancelledError: class extends Error {},
  BridgeTimeoutError: class extends Error {},
  BridgeUnavailableError: class extends Error {},
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

const nessusSettings = {
  serverUrl: 'https://172.20.200.75:8834', requestTimeoutSeconds: 120,
  trustedCertificateThumbprint: '', cacheTtlMinutes: 15, backfillDays: 90,
  retentionDays: 365, staleWarningDays: 14, staleCriticalDays: 30,
  excludedScanIds: [], missingNessusExcludedOuPatterns: [], missingNessusExcludedHostPatterns: [],
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
        maxBatchHosts: 50,
        machineName: 'TEST-PC',
        machineFqdn: 'TEST-PC.corp.example',
        runtimeProfile: 'test',
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
    if (action === 'getNessusSettings') {
      return Promise.resolve({ settings: nessusSettings, restartRequired: false });
    }
    if (action === 'getCredentialStatus') return Promise.resolve({ saved: false });
    if (action === 'getKasperskyCertificate') return Promise.resolve({
      sha256Fingerprint: 'B'.repeat(64), subject: 'CN=ksc',
      validFromUtc: '2026-01-01T00:00:00Z', validToUtc: '2027-01-01T00:00:00Z',
    });
    if (action === 'getCertificate') return Promise.resolve({
      sha256Fingerprint: 'A'.repeat(64), subject: 'CN=nessus',
      validFromUtc: '2026-01-01T00:00:00Z', validToUtc: '2027-01-01T00:00:00Z',
    });
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

function LocationProbe() {
  const location = useLocation();
  return <output data-testid="settings-location">{location.pathname}{location.search}</output>;
}

function renderSettings(initialEntry = '/settings') {
  return render(
    <MemoryRouter initialEntries={[initialEntry]}>
      <TargetProvider>
        <SettingsPage />
      </TargetProvider>
      <LocationProbe />
    </MemoryRouter>,
  );
}

it('shows an error when the log folder cannot be opened', async () => {
  renderSettings();
  const button = await screen.findByRole('button', { name: 'Open log folder' });
  invokeMock.mockRejectedValueOnce(new Error('raw shell association failure'));

  await userEvent.click(button);

  const alert = await screen.findByRole('alert');
  expect(within(alert).getByText('The log folder could not be opened.')).toBeTruthy();
  expect(within(alert).getByText('Next action')).toBeTruthy();
  const details = within(alert).getByText('Technical details').closest('details') as HTMLDetailsElement;
  expect(details.open).toBe(false);
  await userEvent.click(details.querySelector('summary')!);
  expect(within(alert).getByText(/raw shell association failure/)).toBeTruthy();
});

describe('SettingsPage', () => {
  it('keeps configured KSC group names verbatim while the product hint stays English', async () => {
    renderSettings();

    const groups = await screen.findByLabelText('Ignored KSC administration groups') as HTMLInputElement;
    expect(groups.placeholder).toBe('Devices not suitable for Kaspersky');
    expect(groups.value).toBe('Nicht für Kaspersky geeignete Geräte');
  });

  it('provides canonical section links and scrolls a deep link into view after loading', async () => {
    const scrollIntoView = vi.fn();
    Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', {
      configurable: true,
      value: scrollIntoView,
    });

    renderSettings('/settings?section=patch-management');

    const navigation = await screen.findByRole('navigation', { name: 'Settings sections' });
    const labels = [
      'Effective configuration',
      'Environment Health',
      'Vulnerability Management',
      'Patch Management',
      'Configuration policy',
    ];
    for (const label of labels) {
      expect(within(navigation).getByRole('link', { name: label })).toBeTruthy();
    }
    expect(within(navigation).getByRole('link', { name: 'Patch Management' }).getAttribute('aria-current')).toBe('location');
    expect(screen.getByTestId('settings-location').textContent).toBe('/settings?section=patch-management');
    await waitFor(() => expect(scrollIntoView).toHaveBeenCalledWith({ behavior: 'smooth', block: 'start' }));

    await userEvent.click(within(navigation).getByRole('link', { name: 'Vulnerability Management' }));

    expect(screen.getByTestId('settings-location').textContent).toBe('/settings?section=vulnerability-management');
    expect(within(navigation).getByRole('link', { name: 'Vulnerability Management' }).getAttribute('aria-current')).toBe('location');

    scrollIntoView.mockClear();
    await userEvent.click(within(navigation).getByRole('link', { name: 'Effective configuration' }));
    expect(screen.getByTestId('settings-location').textContent).toBe('/settings');
    await waitFor(() => expect(scrollIntoView).toHaveBeenCalledWith({ behavior: 'smooth', block: 'start' }));
  });

  it('falls back from an unknown settings section to the canonical overview URL', async () => {
    renderSettings('/settings?section=not-a-real-section');

    const navigation = await screen.findByRole('navigation', { name: 'Settings sections' });
    expect(within(navigation).getByRole('link', { name: 'Effective configuration' }).getAttribute('aria-current')).toBe('location');
    expect(screen.getByTestId('settings-location').textContent).toBe('/settings');
  });

  it('tracks unsaved settings independently and only clears a section after a successful save', async () => {
    renderSettings();

    const navigation = await screen.findByRole('navigation', { name: 'Settings sections' });
    expect(within(navigation).queryByText('Unsaved changes')).toBeNull();

    await userEvent.type(screen.getByLabelText('Kaspersky Administration Server'), 'ksc.local');
    await userEvent.clear(screen.getByLabelText('Nessus HTTPS URL'));
    await userEvent.type(screen.getByLabelText('Nessus HTTPS URL'), 'https://nessus-new.example.test:8834');
    await userEvent.clear(screen.getByLabelText('opsi server'));
    await userEvent.type(screen.getByLabelText('opsi server'), 'opsi-new.example.test');

    expect(within(navigation).getByRole('link', { name: /Environment Health.*Unsaved changes/ })).toBeTruthy();
    expect(within(navigation).getByRole('link', { name: /Vulnerability Management.*Unsaved changes/ })).toBeTruthy();
    expect(within(navigation).getByRole('link', { name: /Patch Management.*Unsaved changes/ })).toBeTruthy();

    const environmentSection = screen.getByRole('heading', { name: 'IT Lifecycle / Environment Health' }).closest('[id="settings-section-environment-health"]')!;
    expect(within(environmentSection as HTMLElement).getByText('Unsaved changes')).toBeTruthy();

    await userEvent.click(screen.getByRole('button', { name: 'Save IT Lifecycle settings' }));
    await waitFor(() => expect(within(navigation).getByRole('link', { name: 'Environment Health' })).toBeTruthy());
    expect(within(navigation).getByRole('link', { name: /Vulnerability Management.*Unsaved changes/ })).toBeTruthy();

    invokeMock.mockRejectedValueOnce(new Error('save rejected'));
    await userEvent.click(screen.getByRole('button', { name: 'Save Nessus settings' }));
    await screen.findByText('The Nessus settings or connection could not be updated.');
    expect(within(navigation).getByRole('link', { name: /Vulnerability Management.*Unsaved changes/ })).toBeTruthy();
  });

  it('summarizes invalid sections and blocks only their save requests until corrected', async () => {
    renderSettings();

    const navigation = await screen.findByRole('navigation', { name: 'Settings sections' });
    await userEvent.clear(screen.getByLabelText('Stale cleanup candidate (days)'));
    await userEvent.type(screen.getByLabelText('Stale cleanup candidate (days)'), '60');
    await userEvent.clear(screen.getByLabelText('Nessus HTTPS URL'));
    await userEvent.type(screen.getByLabelText('Nessus HTTPS URL'), 'http://nessus.example.test:8834');
    await userEvent.clear(screen.getByLabelText('opsi port'));
    await userEvent.type(screen.getByLabelText('opsi port'), '0');

    const summary = screen.getByRole('alert', { name: 'Settings validation' });
    expect(within(summary).getByText('3 settings problems must be fixed before saving.')).toBeTruthy();
    expect(within(summary).getByRole('link', { name: /Environment Health.*Stale cleanup candidate must be greater/ })).toBeTruthy();
    expect(within(summary).getByRole('link', { name: /Vulnerability Management.*valid HTTPS URL/ })).toBeTruthy();
    expect(within(summary).getByRole('link', { name: /Patch Management.*opsi port must be between 1 and 65535/ })).toBeTruthy();
    expect(within(navigation).getByRole('link', { name: /Environment Health.*1 validation issue/ })).toBeTruthy();
    expect(within(navigation).getByRole('link', { name: /Vulnerability Management.*1 validation issue/ })).toBeTruthy();
    expect(within(navigation).getByRole('link', { name: /Patch Management.*1 validation issue/ })).toBeTruthy();

    const saveButtons = [
      screen.getByRole('button', { name: 'Save IT Lifecycle settings' }),
      screen.getByRole('button', { name: 'Save Nessus settings' }),
      screen.getByRole('button', { name: 'Save opsi settings' }),
    ];
    for (const button of saveButtons) expect((button as HTMLButtonElement).disabled).toBe(true);
    expect(invokeMock.mock.calls.filter((call) => String(call[1]).startsWith('save')).length).toBe(0);

    await userEvent.clear(screen.getByLabelText('Stale cleanup candidate (days)'));
    await userEvent.type(screen.getByLabelText('Stale cleanup candidate (days)'), '90');
    await userEvent.clear(screen.getByLabelText('Nessus HTTPS URL'));
    await userEvent.type(screen.getByLabelText('Nessus HTTPS URL'), 'https://nessus.example.test:8834');
    await userEvent.clear(screen.getByLabelText('opsi port'));
    await userEvent.type(screen.getByLabelText('opsi port'), '4447');

    await waitFor(() => expect(screen.queryByRole('alert', { name: 'Settings validation' })).toBeNull());
    for (const button of saveButtons) expect((button as HTMLButtonElement).disabled).toBe(false);
  });

  it('keeps initial load diagnostics behind actionable guidance', async () => {
    invokeMock.mockRejectedValueOnce(new Error('raw settings bootstrap failure'));

    renderSettings();

    const alert = await screen.findByRole('alert');
    expect(within(alert).getByText('Application settings could not be loaded.')).toBeTruthy();
    expect(within(alert).getByText('Cause')).toBeTruthy();
    expect(within(alert).getByText('Next action')).toBeTruthy();
    const details = within(alert).getByText('Technical details').closest('details') as HTMLDetailsElement;
    expect(details.open).toBe(false);
    await userEvent.click(details.querySelector('summary')!);
    expect(within(alert).getByText(/raw settings bootstrap failure/)).toBeTruthy();
  });

  it('keeps Nessus provider diagnostics local and collapsed', async () => {
    renderSettings();
    const heading = await screen.findByRole('heading', { name: 'Nessus / Vulnerability Management' });
    const section = heading.closest('[id="settings-section-vulnerability-management"]') as HTMLElement;
    const readCertificate = within(section).getByRole('button', { name: 'Read HTTPS certificate fingerprint' });
    invokeMock.mockRejectedValueOnce(new Error('raw TLS provider failure'));

    await userEvent.click(readCertificate);

    const alert = await screen.findByRole('alert');
    expect(within(alert).getByText('The Nessus settings or connection could not be updated.')).toBeTruthy();
    expect(within(alert).getByText('Cause')).toBeTruthy();
    expect(within(alert).getByText('Next action')).toBeTruthy();
    const details = within(alert).getByText('Technical details').closest('details') as HTMLDetailsElement;
    expect(details.open).toBe(false);
    await userEvent.click(details.querySelector('summary')!);
    expect(within(alert).getByText(/raw TLS provider failure/)).toBeTruthy();
  });

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

  it('requires an explicit danger confirmation before deleting each saved credential', async () => {
    const baseImplementation = invokeMock.getMockImplementation()!;
    invokeMock.mockImplementation((module: string, action: string, payload?: unknown) => {
      if (action === 'getServiceCredentialStatuses') {
        return Promise.resolve({
          kaspersky: { saved: true, userName: 'ksc-reader', domain: 'CORP' },
          opsi: { saved: true, userName: 'opsi-reader', domain: null },
        });
      }
      if (action === 'getCredentialStatus') return Promise.resolve({ saved: true });
      if (action === 'deleteServiceCredential') {
        return Promise.resolve({ saved: false, userName: null, domain: null });
      }
      if (action === 'deleteCredential') return Promise.resolve({ saved: false });
      return baseImplementation(module, action, payload);
    });
    renderSettings();

    const kscDelete = await screen.findByRole('button', { name: 'Remove saved KSC credential' });
    const opsiDelete = screen.getByRole('button', { name: 'Remove saved opsi credential' });
    const nessusDelete = screen.getByRole('button', { name: 'Remove Nessus API keys' });
    for (const button of [kscDelete, opsiDelete, nessusDelete]) {
      expect(button.className).toContain('bg-fail-700');
    }

    await userEvent.click(kscDelete);
    expect(invokeMock.mock.calls.filter((call) => call[1] === 'deleteServiceCredential')).toHaveLength(0);
    expect(screen.getByText(/Automatic Environment Health access will stop/)).toBeTruthy();
    const cancelKscRemoval = screen.getByRole('button', { name: 'Cancel removing saved KSC credential' });
    expect(document.activeElement).toBe(cancelKscRemoval);
    await userEvent.click(cancelKscRemoval);
    expect(invokeMock.mock.calls.filter((call) => call[1] === 'deleteServiceCredential')).toHaveLength(0);

    await userEvent.click(screen.getByRole('button', { name: 'Remove saved KSC credential' }));
    await userEvent.click(screen.getByRole('button', { name: 'Confirm removal of saved KSC credential' }));
    expect(invokeMock).toHaveBeenCalledWith('system', 'deleteServiceCredential', { kind: 'KASPERSKY' });

    await userEvent.click(screen.getByRole('button', { name: 'Remove saved opsi credential' }));
    expect(screen.getByText(/Automatic opsi connections will stop/)).toBeTruthy();
    await userEvent.click(screen.getByRole('button', { name: 'Confirm removal of saved opsi credential' }));
    expect(invokeMock).toHaveBeenCalledWith('system', 'deleteServiceCredential', { kind: 'OPSI' });

    await userEvent.click(screen.getByRole('button', { name: 'Remove Nessus API keys' }));
    expect(screen.getByText(/Nessus synchronization will stop/)).toBeTruthy();
    await userEvent.click(screen.getByRole('button', { name: 'Confirm removal of Nessus API keys' }));
    expect(invokeMock).toHaveBeenCalledWith('vulnerabilitymanagement', 'deleteCredential', {});
  });

  it('reads the Nessus certificate fingerprint from the currently entered server', async () => {
    renderSettings();

    const heading = await screen.findByRole('heading', { name: 'Nessus / Vulnerability Management' });
    const section = heading.closest('[id="settings-section-vulnerability-management"]') as HTMLElement;
    await userEvent.click(within(section).getByRole('button', { name: 'Read HTTPS certificate fingerprint' }));

    expect(invokeMock).toHaveBeenCalledWith('vulnerabilitymanagement', 'getCertificate', {
      serverUrl: 'https://172.20.200.75:8834', requestTimeoutSeconds: 30,
    }, 35_000);
    expect(await screen.findByText(`SHA-256: ${'A'.repeat(64)}`)).toBeTruthy();
    expect((screen.getByLabelText('Certificate fingerprint') as HTMLInputElement).value).toBe('A'.repeat(64));
  });

  it('reads the Kaspersky certificate fingerprint from the currently entered server', async () => {
    renderSettings();

    const server = await screen.findByLabelText('Kaspersky Administration Server');
    await userEvent.type(server, 'ksc.example.test');
    const heading = screen.getByRole('heading', { name: 'IT Lifecycle / Environment Health' });
    const section = heading.closest('[id="settings-section-environment-health"]') as HTMLElement;
    await userEvent.click(within(section).getByRole('button', { name: 'Read HTTPS certificate fingerprint' }));

    expect(invokeMock).toHaveBeenCalledWith('employeelifecycle', 'getKasperskyCertificate', {
      server: 'ksc.example.test', port: 13299, requestTimeoutSeconds: 30,
    }, 35_000);
    expect(await within(section).findByText(`SHA-256: ${'B'.repeat(64)}`)).toBeTruthy();
    expect((screen.getByLabelText('KSC certificate thumbprint') as HTMLInputElement).value).toBe('B'.repeat(64));
  });
});
