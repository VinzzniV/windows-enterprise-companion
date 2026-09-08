import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { EventLogSection } from './EventLogSection';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../../shared/bridge/bridgeClient', () => ({
  invoke: invokeMock,
  BridgeInvokeError: class extends Error {},
  BridgeCancelledError: class extends Error {},
  BridgeTimeoutError: class extends Error {},
  BridgeUnavailableError: class extends Error {},
}));

describe('EventLogSection', () => {
  beforeEach(() => invokeMock.mockReset());

  it('opens safe full text with host, time, source, truncation and clipboard feedback', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } });
    const message = '<script>alert("unsafe")</script> diagnostic text …';
    invokeMock.mockResolvedValue({
      presetKey: 'system-errors',
      totalMatched: 1,
      truncated: false,
      entries: [{
        timeGenerated: '2026-09-08T08:30:00Z',
        level: 'Error',
        source: 'Service Control Manager',
        eventCode: 7000,
        message,
      }],
    });

    render(<EventLogSection host="PC-041.corp.example" target={{ host: 'PC-041.corp.example' }} />);
    await userEvent.click(screen.getByRole('button', { name: 'Run query' }));
    const open = await screen.findByRole('button', { name: 'View full message from Service Control Manager, event 7000' });
    await userEvent.click(open);

    const dialog = screen.getByRole('dialog', { name: 'Event 7000 on PC-041.corp.example' });
    expect(within(dialog).getByText('PC-041.corp.example')).toBeDefined();
    expect(within(dialog).getAllByText('Service Control Manager').length).toBeGreaterThan(0);
    expect(within(dialog).getByText(message)).toBeDefined();
    expect(dialog.querySelector('script')).toBeNull();
    expect(within(dialog).getByText(/500-character detail limit/)).toBeDefined();

    await userEvent.click(within(dialog).getByRole('button', { name: 'Copy message' }));
    expect(writeText).toHaveBeenCalledWith(message);
    expect(within(dialog).getByText('Message copied.')).toBeDefined();

    await userEvent.click(within(dialog).getByRole('button', { name: 'Close event message' }));
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(document.activeElement).toBe(open);
  });
});
