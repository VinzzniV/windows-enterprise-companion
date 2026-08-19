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
  securityCoverage: {
    isKnown: true,
    totalChecks: 13,
    succeededChecks: 12,
    failedChecks: 1,
    requiresElevationChecks: 0,
    notApplicableChecks: 0,
    applicableChecks: 13,
    isComplete: false,
  },
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
      { openAfterExport: true, host: null },
    );
  });

  it('treats a cancelled save dialog as a neutral outcome, not an error', async () => {
    setUpInvoke({ cancelled: true, filePath: null });

    render(<ReportingPage />);
    await userEvent.click(await screen.findByRole('button', { name: 'Export JSON' }));

    expect(await screen.findByText('Export cancelled.')).toBeDefined();
  });

  it('shows incomplete security coverage in the report overview', async () => {
    setUpInvoke({ cancelled: true, filePath: null });

    render(<ReportingPage />);

    expect(await screen.findByText(/12\/13 applicable checks evaluated — incomplete/)).toBeDefined();
  });

  it('disables exports when there is nothing to export yet', async () => {
    invokeMock.mockResolvedValue({
      inventoryCapturedAtUtc: null,
      securityScanCompletedAtUtc: null,
      securityScanStatus: null,
      securityFindingCount: null,
      securityCoverage: null,
    } satisfies ReportOverview);

    render(<ReportingPage />);

    const exportButton = await screen.findByRole('button', { name: 'Export HTML' });
    expect(exportButton.hasAttribute('disabled')).toBe(true);
  });
});
