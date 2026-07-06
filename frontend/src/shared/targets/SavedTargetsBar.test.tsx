import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { TargetProvider } from './TargetContext';
import { SavedTargetsBar } from './SavedTargetsBar';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../bridge/bridgeClient', () => ({
  invoke: invokeMock,
  BridgeInvokeError: class extends Error {},
}));

describe('SavedTargetsBar', () => {
  beforeEach(() => invokeMock.mockReset());

  it('shows saved targets of the role, picks one, and saves the current host', async () => {
    invokeMock.mockImplementation((module: string, action: string) => {
      if (module === 'targets' && action === 'list') {
        return Promise.resolve({
          targets: [
            { id: 1, label: 'PRSRV1', host: 'PRSRV1', role: 'PrintServer', userName: 'svc', createdAtUtc: 'x' },
            { id: 2, label: 'DC1', host: 'dc1', role: 'DomainController', userName: null, createdAtUtc: 'x' },
          ],
        });
      }
      return Promise.resolve({ targets: [] });
    });
    const onPick = vi.fn();

    render(
      <TargetProvider>
        <SavedTargetsBar role="PrintServer" currentHost="NEW-SRV" onPick={onPick} />
      </TargetProvider>,
    );

    // Only the PrintServer chip is shown, not the DomainController one
    expect(await screen.findByText('PRSRV1')).toBeDefined();
    expect(screen.queryByText('DC1')).toBeNull();

    fireEvent.click(screen.getByText('PRSRV1'));
    expect(onPick).toHaveBeenCalledWith(expect.objectContaining({ host: 'PRSRV1', userName: 'svc' }));

    fireEvent.click(screen.getByRole('button', { name: /Save/ }));
    expect(invokeMock).toHaveBeenCalledWith('targets', 'save', {
      label: 'NEW-SRV',
      host: 'NEW-SRV',
      role: 'PrintServer',
      userName: null,
    });
  });

  it('renders nothing outside a TargetProvider', () => {
    const { container } = render(
      <SavedTargetsBar role="PrintServer" currentHost="x" onPick={() => {}} />,
    );
    expect(container.firstChild).toBeNull();
  });
});
