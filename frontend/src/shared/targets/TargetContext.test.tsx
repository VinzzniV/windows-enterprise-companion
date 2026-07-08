import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { TargetProvider, useTargets } from './TargetContext';
import type { SavedTarget } from '../api-types';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../bridge/bridgeClient', () => ({
  invoke: invokeMock,
  BridgeInvokeError: class extends Error {},
}));

const printServer: SavedTarget = {
  id: 1,
  label: 'Print',
  host: 'PRSRV1',
  role: 'PrintServer',
  userName: null,
  createdAtUtc: '2026-07-06T10:00:00Z',
};

function Consumer() {
  const t = useTargets();
  return (
    <div>
      <span data-testid="ready">{String(t.savedTargetsReady)}</span>
      <ul>
        {t.savedTargets.map((s) => (
          <li key={s.id}>{s.label}</li>
        ))}
      </ul>
      <span data-testid="cred">{t.credentialsFor('DESKTOP-1')?.userName ?? 'none'}</span>
      <button
        onClick={() => t.signInAdmin({ userName: 'admin', domain: 'CORP', password: 'x' })}
      >
        signin
      </button>
      <button onClick={() => t.signOutAdmin()}>signout</button>
      <button onClick={() => void t.saveTarget({ label: 'DC1', host: 'dc1', role: 'DomainController' })}>
        save
      </button>
    </div>
  );
}

describe('TargetContext', () => {
  beforeEach(() => invokeMock.mockReset());

  it('loads saved targets on mount', async () => {
    invokeMock.mockResolvedValue({ targets: [printServer] });
    render(
      <TargetProvider>
        <Consumer />
      </TargetProvider>,
    );
    expect(await screen.findByText('Print')).toBeDefined();
    expect(screen.getByTestId('ready').textContent).toBe('true');
  });

  it('holds one global admin credential for every host and signs out', async () => {
    invokeMock.mockResolvedValue({ targets: [] });
    render(
      <TargetProvider>
        <Consumer />
      </TargetProvider>,
    );
    await screen.findByTestId('ready');

    expect(screen.getByTestId('cred').textContent).toBe('none');
    fireEvent.click(screen.getByText('signin'));
    expect(screen.getByTestId('cred').textContent).toBe('admin'); // reused for any host
    fireEvent.click(screen.getByText('signout'));
    expect(screen.getByTestId('cred').textContent).toBe('none');
  });

  it('saveTarget calls targets/save and applies the returned list', async () => {
    invokeMock.mockImplementation((module: string, action: string) => {
      if (module === 'targets' && action === 'save') {
        return Promise.resolve({
          targets: [
            {
              id: 2,
              label: 'DC1',
              host: 'dc1',
              role: 'DomainController',
              userName: null,
              createdAtUtc: '2026-07-06T10:00:00Z',
            },
          ],
        });
      }
      // list (and any framework-injected noise) resolves to an empty set
      return Promise.resolve({ targets: [] });
    });
    render(
      <TargetProvider>
        <Consumer />
      </TargetProvider>,
    );
    await screen.findByTestId('ready');
    fireEvent.click(screen.getByText('save'));
    expect(await screen.findByText('DC1')).toBeDefined();
    expect(invokeMock).toHaveBeenCalledWith('targets', 'save', {
      label: 'DC1',
      host: 'dc1',
      role: 'DomainController',
      userName: null,
    });
  });
});
