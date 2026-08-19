import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { AppInfoFooter, type AppInfoState } from './App';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../shared/bridge/bridgeClient', () => ({
  invoke: invokeMock,
  BridgeInvokeError: class extends Error {},
}));

vi.mock('../shared/bridge/errorText', () => ({
  errorText: (error: unknown) => error instanceof Error ? error.message : String(error),
}));

const loadedState: AppInfoState = {
  kind: 'loaded',
  appInfo: {
    version: '1.2.3',
    databasePath: 'C:\\private\\wec.db',
    logDirectory: 'C:\\private\\logs',
    isElevated: false,
    maxParallelScans: 4,
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
    render(<AppInfoFooter state={{ kind: 'error', message: 'Bridge unavailable' }} />);

    expect(screen.getByRole('alert').textContent).toContain('Bridge unavailable');
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
        onRejected(new Error('Folder cannot be opened'));
        return Promise.resolve();
      },
    });
    render(<AppInfoFooter state={loadedState} />);
    expect(invokeMock).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole('button', { name: 'Open log folder' }));

    expect((await screen.findByRole('alert')).textContent).toContain('Folder cannot be opened');
  });
});
