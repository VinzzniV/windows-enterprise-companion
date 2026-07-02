import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReportExportResult, ReportOverview } from '../../shared/api-types';
import { ReportingPage } from './ReportingPage';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../shared/bridge/bridgeClient', () => ({ invoke: invokeMock }));

const overviewWithData: ReportOverview = {
  inventoryCapturedAtUtc: '2026-07-02T18:00:00Z',
  securityScanCompletedAtUtc: '2026-07-02T18:05:00Z',
  securityScanStatus: 'Completed',
  securityFindingCount: 3,
};

function setUpInvoke(exportResult: ReportExportResult): void {
  invokeMock.mockImplementation((_module: string, action: string) =>
    action === 'getOverview'
      ? Promise.resolve(overviewWithData)
      : Promise.resolve(exportResult),
  );
}

describe('ReportingPage', () => {
  beforeEach(() => {
    invokeMock.mockReset();
  });

  it('shows the export result path after a successful export', async () => {
    setUpInvoke({ cancelled: false, filePath: 'C:\\reports\\wec.html' });

    render(<ReportingPage />);
    await userEvent.click(await screen.findByRole('button', { name: 'Export HTML' }));

    expect(await screen.findByText('C:\\reports\\wec.html')).toBeDefined();
    expect(invokeMock).toHaveBeenCalledWith(
      'reporting',
      'exportHtml',
      { openAfterExport: true },
      expect.any(Number),
    );
  });

  it('treats a cancelled save dialog as a neutral outcome, not an error', async () => {
    setUpInvoke({ cancelled: true, filePath: null });

    render(<ReportingPage />);
    await userEvent.click(await screen.findByRole('button', { name: 'Export JSON' }));

    expect(await screen.findByText('Export cancelled.')).toBeDefined();
  });

  it('disables exports when there is nothing to export yet', async () => {
    invokeMock.mockResolvedValue({
      inventoryCapturedAtUtc: null,
      securityScanCompletedAtUtc: null,
      securityScanStatus: null,
      securityFindingCount: null,
    } satisfies ReportOverview);

    render(<ReportingPage />);

    const exportButton = await screen.findByRole('button', { name: 'Export HTML' });
    expect(exportButton.hasAttribute('disabled')).toBe(true);
  });
});
