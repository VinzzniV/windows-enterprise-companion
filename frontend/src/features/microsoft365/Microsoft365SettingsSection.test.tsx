import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, expect, it, vi } from 'vitest';
import { BridgeInvokeError } from '../../shared/bridge/bridgeClient';
import { Microsoft365SettingsSection } from './Microsoft365SettingsSection';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));
vi.mock('../../shared/bridge/bridgeClient', async importOriginal => ({
  ...await importOriginal<typeof import('../../shared/bridge/bridgeClient')>(), invoke: invokeMock,
}));
const settings = { tenantId: '', clientId: '', enableIntune: false, enableAuthenticationReports: false };
beforeEach(() => { invokeMock.mockReset(); invokeMock.mockResolvedValue({ settings, restartRequired: false }); });

it('saves only identifiers and flags through Settings and reports the required restart', async () => {
  const actor = userEvent.setup();
  const onDirtyChange = vi.fn();
  render(<MemoryRouter><Microsoft365SettingsSection onDirtyChange={onDirtyChange} /></MemoryRouter>);
  await actor.type(await screen.findByLabelText('Default tenant ID'), '11111111-1111-1111-1111-111111111111');
  await actor.type(screen.getByLabelText('Default client ID'), '22222222-2222-2222-2222-222222222222');
  expect(onDirtyChange).toHaveBeenLastCalledWith(true);
  invokeMock.mockResolvedValueOnce({ settings, restartRequired: true });
  await actor.click(screen.getByRole('button', { name: 'Save Microsoft 365 defaults' }));
  expect(invokeMock).toHaveBeenLastCalledWith('system', 'saveMicrosoft365Settings', { settings: {
    ...settings, tenantId: '11111111-1111-1111-1111-111111111111', clientId: '22222222-2222-2222-2222-222222222222',
  } });
  expect((await screen.findByRole('status')).textContent).toContain('Restart WEC');
  expect(invokeMock.mock.calls.every(call => call[0] === 'system')).toBe(true);
});

it('keeps invalid-input details and unsaved values visible at the save control', async () => {
  const actor = userEvent.setup();
  render(<MemoryRouter><Microsoft365SettingsSection onDirtyChange={vi.fn()} /></MemoryRouter>);
  await actor.type(await screen.findByLabelText('Default tenant ID'), 'wrong-tenant');
  invokeMock.mockRejectedValueOnce(new BridgeInvokeError({ code: 'INVALID_REQUEST', message: 'Tenant ID and Client ID must both be GUIDs.' }));
  await actor.click(screen.getByRole('button', { name: 'Save Microsoft 365 defaults' }));
  expect((await screen.findByRole('alert')).textContent).toContain('must both be GUIDs');
  expect((screen.getByLabelText('Default tenant ID') as HTMLInputElement).value).toBe('wrong-tenant');
});

it('offers a local retry when loading defaults fails', async () => {
  invokeMock.mockRejectedValueOnce(new Error('fixture failure'));
  render(<MemoryRouter><Microsoft365SettingsSection onDirtyChange={vi.fn()} /></MemoryRouter>);
  await userEvent.click(await screen.findByRole('button', { name: 'Retry defaults' }));
  await waitFor(() => expect(screen.getByLabelText('Default tenant ID')).toBeTruthy());
  expect(screen.queryByRole('alert')).toBeNull();
});
