import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { LogEntry, RecentLogEntriesResult } from '../../shared/api-types';
import { ErrorLogPage } from './ErrorLogPage';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../shared/bridge/bridgeClient', () => ({
  invoke: invokeMock,
  BridgeInvokeError: class extends Error {},
  BridgeCancelledError: class extends Error {},
  BridgeTimeoutError: class extends Error {},
  BridgeUnavailableError: class extends Error {},
}));

function logResult(
  entries: LogEntry[],
  overrides: Partial<RecentLogEntriesResult> = {},
): RecentLogEntriesResult {
  return {
    entries,
    source: 'wec-20260819.log',
    clearedAtUtc: null,
    coverage: {
      availableFileCount: 1,
      evaluatedFileCount: 1,
      fileSelectionTruncated: false,
      evaluatedBytes: 4096,
      byteWindowTruncated: false,
      resultLimit: 500,
      totalMatched: entries.length,
      resultTruncated: false,
      truncatedDetailCount: entries.filter((entry) => entry.technicalDetailsTruncated).length,
    },
    ...overrides,
  };
}

describe('ErrorLogPage', () => {
  beforeEach(() => invokeMock.mockReset());

  it('keeps the table compact and opens complete raw evidence on demand', async () => {
    invokeMock.mockResolvedValue(logResult([{
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
        technicalDetailsTruncated: false,
      }]));

    render(<ErrorLogPage />);

    const summary = await screen.findByText('Printer scan failed.');
    expect(screen.getByText('Wec.Infrastructure.Wmi')).toBeDefined();
    expect(screen.queryByText(/SELECT \* FROM MSFT_Printer/)).toBeNull();

    await userEvent.click(summary.closest('tr')!);

    const dialog = screen.getByRole('dialog', { name: 'Log entry details' });
    expect(document.activeElement).toBe(dialog);
    expect(screen.getByText(/SELECT \* FROM MSFT_Printer/)).toBeDefined();
    expect(screen.getByText(/"host":"PC-041"/)).toBeDefined();
    expect(summary.closest('tr')?.getAttribute('aria-selected')).toBe('true');

    await userEvent.click(screen.getByRole('button', { name: 'Close log entry details' }));
    expect(screen.queryByRole('dialog', { name: 'Log entry details' })).toBeNull();
    expect(document.activeElement).toBe(summary.closest('tr'));
  });

  it('keeps load diagnostics collapsed and offers a local retry', async () => {
    invokeMock
      .mockRejectedValueOnce(new Error('raw log read failure'))
      .mockResolvedValue(logResult([], { source: null }));

    render(<ErrorLogPage />);

    const alert = await screen.findByRole('alert');
    expect(within(alert).getByText('The error log could not be loaded.')).toBeDefined();
    expect(within(alert).getByText('Next action')).toBeDefined();
    expect(within(alert).getByRole('button', { name: 'Retry loading' })).toBeDefined();
    const details = within(alert).getByText('Technical details').closest('details') as HTMLDetailsElement;
    expect(details.open).toBe(false);
  });

  it('opens the first of 500 rows in the viewport-bound dialog', async () => {
    const entries = Array.from({ length: 500 }, (_, index) => ({
        timestamp: `2026-08-19 16:${String(index % 60).padStart(2, '0')}:00.000 +02:00`,
        level: 'ERR',
        source: `Source ${index}`,
        summary: `Failure ${index}`,
        technicalDetails: `Technical evidence ${index}`,
        technicalDetailsTruncated: false,
      }));
    invokeMock.mockResolvedValue(logResult(entries));
    render(<ErrorLogPage />);
    const firstSummary = await screen.findByText('Failure 0');

    await userEvent.click(firstSummary.closest('tr')!);

    const dialog = screen.getByRole('dialog', { name: 'Log entry details' });
    expect(dialog.className).toContain('max-h-[calc(100dvh-1rem)]');
    expect(within(dialog).getByText('Technical evidence 0')).toBeDefined();
  });

  it('explains and performs the view-only history boundary without claiming to delete logs', async () => {
    const initialResult = logResult([{
        timestamp: '2026-08-19 16:12:00.000 +02:00',
        level: 'ERR',
        source: 'Wec.Infrastructure.Wmi',
        summary: 'Printer scan failed.',
        technicalDetails: 'raw printer failure',
        technicalDetailsTruncated: false,
      }]);
    invokeMock
      .mockResolvedValueOnce(initialResult)
      .mockResolvedValueOnce({ clearedAtUtc: '2026-08-20T08:30:00Z' })
      .mockResolvedValueOnce(logResult([], { clearedAtUtc: '2026-08-20T08:30:00Z' }));

    render(<ErrorLogPage />);
    expect(await screen.findByText('Printer scan failed.')).toBeDefined();
    expect(screen.getByText('Sets a local visibility marker at the current time. All earlier entries are hidden from this view; log files stay on disk.')).toBeDefined();
    expect(screen.queryByRole('button', { name: 'Clear' })).toBeNull();

    await userEvent.click(screen.getByRole('button', { name: 'Hide previous entries' }));

    await waitFor(() => expect(invokeMock).toHaveBeenCalledTimes(3));
    expect(invokeMock.mock.calls[1]).toEqual(['logs', 'clearRecent', {}]);
    expect(await screen.findByText(/Previous entries hidden since/)).toBeDefined();
    expect(screen.getByText('No new entries')).toBeDefined();
    expect(screen.getByText('No warnings or errors have been logged since previous entries were hidden.')).toBeDefined();
  });

  it('keeps loaded entries visible when clearing fails', async () => {
    const result = logResult([{
        timestamp: '2026-08-19 16:12:00.000 +02:00',
        level: 'ERR',
        source: 'Wec.Infrastructure.Wmi',
        summary: 'Printer scan failed.',
        technicalDetails: 'raw printer failure',
        technicalDetailsTruncated: false,
      }]);
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

  it('applies the error filter before the result limit', async () => {
    const warnings = Array.from({ length: 500 }, (_, index): LogEntry => ({
      timestamp: `2026-08-19 16:${String(index % 60).padStart(2, '0')}:00.000 +02:00`,
      level: 'WRN',
      source: 'Wec.WarningSource',
      summary: `Warning ${index}`,
      technicalDetails: `Warning ${index}`,
      technicalDetailsTruncated: false,
    }));
    const relevantError: LogEntry = {
      timestamp: '2026-08-19 08:00:00.000 +02:00',
      level: 'ERR',
      source: 'Wec.ErrorSource',
      summary: 'Older relevant error',
      technicalDetails: 'Older relevant error',
      technicalDetailsTruncated: false,
    };
    invokeMock
      .mockResolvedValueOnce(logResult(warnings, {
        coverage: {
          ...logResult([]).coverage,
          totalMatched: 501,
          resultTruncated: true,
        },
      }))
      .mockResolvedValueOnce(logResult([relevantError]));

    render(<ErrorLogPage />);
    await screen.findByText('Warning 0');
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Log level filter' }), 'errors');

    expect(await screen.findByText('Older relevant error')).toBeDefined();
    expect(invokeMock).toHaveBeenLastCalledWith('logs', 'recent', { limit: 500, levelFilter: 'ERRORS' });
  });

  it('discloses file, byte, result and per-entry coverage limits', async () => {
    const entry: LogEntry = {
      timestamp: '2026-08-19 16:12:00.000 +02:00',
      level: 'ERR',
      source: 'Wec.Infrastructure.Wmi',
      summary: 'Bounded failure',
      technicalDetails: 'line 1\nline 40',
      technicalDetailsTruncated: true,
    };
    invokeMock.mockResolvedValue(logResult([entry], {
      coverage: {
        availableFileCount: 9,
        evaluatedFileCount: 7,
        fileSelectionTruncated: true,
        evaluatedBytes: 4194304,
        byteWindowTruncated: true,
        resultLimit: 500,
        totalMatched: 725,
        resultTruncated: true,
        truncatedDetailCount: 1,
      },
    }));

    render(<ErrorLogPage />);
    const summary = await screen.findByText('Bounded failure');
    expect(screen.getByText(/Evaluated 7 of 9 retained log files/)).toBeDefined();
    expect(screen.getByText(/Coverage is limited/)).toBeDefined();
    expect(screen.getByText(/Showing the newest 1 matching entries/)).toBeDefined();
    expect(screen.getByText(/1 shown entry has truncated continuation details/)).toBeDefined();

    await userEvent.click(summary.closest('tr')!);
    expect(screen.getByText(/configured per-entry line limit/)).toBeDefined();
  });
});
