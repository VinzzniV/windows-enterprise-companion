import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ErrorLogPage } from './ErrorLogPage';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../shared/bridge/bridgeClient', () => ({
  invoke: invokeMock,
  BridgeInvokeError: class extends Error {},
  BridgeCancelledError: class extends Error {},
  BridgeTimeoutError: class extends Error {},
  BridgeUnavailableError: class extends Error {},
}));

describe('ErrorLogPage', () => {
  beforeEach(() => invokeMock.mockReset());

  it('keeps the table compact and opens complete raw evidence on demand', async () => {
    invokeMock.mockResolvedValue({
      entries: [{
        timestamp: '2026-08-19 16:12:00.000 +02:00',
        level: 'ERR',
        source: 'Wec.Infrastructure.Wmi',
        summary: 'Printer scan failed.',
        technicalDetails: [
          'Wec.Infrastructure.Wmi: Printer scan failed.',
          'WQL: SELECT * FROM MSFT_Printer',
          '{"host":"PC-041"}',
          '   at Wec.Scan()',
        ].join('\n'),
      }],
      source: 'wec-20260819.log',
      clearedAtUtc: null,
    });

    render(<ErrorLogPage />);

    const summary = await screen.findByText('Printer scan failed.');
    expect(screen.getByText('Wec.Infrastructure.Wmi')).toBeDefined();
    expect(screen.queryByText(/SELECT \* FROM MSFT_Printer/)).toBeNull();

    await userEvent.click(summary.closest('tr')!);

    expect(screen.getByRole('region', { name: 'Log entry details' })).toBeDefined();
    expect(screen.getByText(/SELECT \* FROM MSFT_Printer/)).toBeDefined();
    expect(screen.getByText(/"host":"PC-041"/)).toBeDefined();
    expect(summary.closest('tr')?.getAttribute('aria-selected')).toBe('true');

    await userEvent.click(screen.getByRole('button', { name: 'Close log entry details' }));
    expect(screen.queryByRole('region', { name: 'Log entry details' })).toBeNull();
  });

  it('keeps load diagnostics collapsed and offers a local retry', async () => {
    invokeMock
      .mockRejectedValueOnce(new Error('raw log read failure'))
      .mockResolvedValue({ entries: [], source: null, clearedAtUtc: null });

    render(<ErrorLogPage />);

    const alert = await screen.findByRole('alert');
    expect(within(alert).getByText('The error log could not be loaded.')).toBeDefined();
    expect(within(alert).getByText('Next action')).toBeDefined();
    expect(within(alert).getByRole('button', { name: 'Retry loading' })).toBeDefined();
    const details = within(alert).getByText('Technical details').closest('details') as HTMLDetailsElement;
    expect(details.open).toBe(false);
  });

  it('explains and performs the view-only history boundary without claiming to delete logs', async () => {
    const initialResult = {
      entries: [{
        timestamp: '2026-08-19 16:12:00.000 +02:00',
        level: 'ERR',
        source: 'Wec.Infrastructure.Wmi',
        summary: 'Printer scan failed.',
        technicalDetails: 'raw printer failure',
      }],
      source: 'wec-20260819.log',
      clearedAtUtc: null,
    };
    invokeMock
      .mockResolvedValueOnce(initialResult)
      .mockResolvedValueOnce({ clearedAtUtc: '2026-08-20T08:30:00Z' })
      .mockResolvedValueOnce({
        entries: [],
        source: 'wec-20260819.log',
        clearedAtUtc: '2026-08-20T08:30:00Z',
      });

    render(<ErrorLogPage />);
    expect(await screen.findByText('Printer scan failed.')).toBeDefined();
    expect(screen.getByText('Hides the entries currently shown. Log files stay on disk, and new warnings and errors will appear here.')).toBeDefined();
    expect(screen.queryByRole('button', { name: 'Clear' })).toBeNull();

    await userEvent.click(screen.getByRole('button', { name: 'Hide previous entries' }));

    await waitFor(() => expect(invokeMock).toHaveBeenCalledTimes(3));
    expect(invokeMock.mock.calls[1]).toEqual(['logs', 'clearRecent', {}]);
    expect(await screen.findByText(/Previous entries hidden since/)).toBeDefined();
    expect(screen.getByText('No new entries')).toBeDefined();
    expect(screen.getByText('No warnings or errors have been logged since previous entries were hidden.')).toBeDefined();
  });

  it('keeps loaded entries visible when clearing fails', async () => {
    const result = {
      entries: [{
        timestamp: '2026-08-19 16:12:00.000 +02:00',
        level: 'ERR',
        source: 'Wec.Infrastructure.Wmi',
        summary: 'Printer scan failed.',
        technicalDetails: 'raw printer failure',
      }],
      source: 'wec-20260819.log',
      clearedAtUtc: null,
    };
    invokeMock.mockResolvedValueOnce(result).mockRejectedValueOnce(new Error('raw clear marker failure'));

    render(<ErrorLogPage />);
    expect(await screen.findByText('Printer scan failed.')).toBeDefined();
    await userEvent.click(screen.getByRole('button', { name: 'Hide previous entries' }));

    expect(screen.getByText('Printer scan failed.')).toBeDefined();
    const alert = await screen.findByRole('alert');
    expect(within(alert).getByText('The previous error-log entries could not be hidden.')).toBeDefined();
    expect(within(alert).getByText('Next action')).toBeDefined();
    expect(within(alert).getByText('Technical details').closest('details')?.hasAttribute('open')).toBe(false);
  });
});
