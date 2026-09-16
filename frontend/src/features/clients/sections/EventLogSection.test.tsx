import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { EventLogSection } from './EventLogSection';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../../shared/bridge/bridgeClient', () => ({
  invoke: invokeMock,
  BridgeInvokeError: class extends Error {},
  BridgeTimeoutError: class extends Error {},
  BridgeUnavailableError: class extends Error {},
  BridgeCancelledError: class extends Error {},
}));

describe('EventLogSection', () => {
  beforeEach(() => {
    invokeMock.mockReset();
  });

  it('opens a long message from the keyboard and returns focus after Escape', async () => {
    const message = `Beginning of message ${'diagnostic detail '.repeat(80)}end of message`;
    invokeMock.mockResolvedValue({
      presetKey: 'system-errors',
      totalMatched: 1,
      truncated: false,
      entries: [{
        timeGenerated: '2026-09-16T08:00:00Z',
        level: 'Error',
        source: 'Service Control Manager',
        eventCode: 7000,
        message,
      }],
    });

    render(<EventLogSection target={{ host: 'PC-041' }} />);
    await userEvent.click(screen.getByRole('button', { name: 'Run query' }));
    const trigger = await screen.findByRole('button', { name: 'View full message' });
    trigger.focus();
    await userEvent.keyboard('{Enter}');

    const dialog = screen.getByRole('dialog', { name: 'Event 7000 message' });
    expect(dialog.textContent).toContain('end of message');
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Close message' }));

    await userEvent.keyboard('{Escape}');
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(document.activeElement).toBe(trigger);
  });
});
