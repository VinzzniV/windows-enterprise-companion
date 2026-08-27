import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import type { ReportExportResult, ReportOverview } from '../../shared/api-types';
import { ReportingPage } from './ReportingPage';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../shared/bridge/bridgeClient', () => ({
  invoke: invokeMock,
  BridgeInvokeError: class extends Error {},
  BridgeCancelledError: class extends Error {},
  BridgeTimeoutError: class extends Error {},
  BridgeUnavailableError: class extends Error {},
}));

const overviewWithData: ReportOverview = {
  subjectHost: 'WEC-HOST',
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
  readiness: {
    evaluatedAtUtc: '2026-07-02T18:10:00Z',
    isReady: false,
    sources: [
      {
        source: 'Hardware inventory',
        provenance: 'Persisted WMI/CIM inventory snapshot',
        state: 'READY',
        capturedAtUtc: '2026-07-02T18:00:00Z',
        ageSeconds: 600,
        isComplete: true,
        summary: 'Available, complete, and current.',
      },
      {
        source: 'Security posture',
        provenance: 'Persisted Security scan and per-check outcomes',
        state: 'INCOMPLETE',
        capturedAtUtc: '2026-07-02T18:05:00Z',
        ageSeconds: 300,
        isComplete: false,
        summary: 'One applicable check failed.',
      },
    ],
  },
};

function setUpInvoke(exportResult: ReportExportResult): void {
  invokeMock.mockImplementation((_module: string, action: string) => {
    return action === 'getOverview'
      ? Promise.resolve(overviewWithData)
      : Promise.resolve(exportResult);
  });
}

function renderPage() {
  return render(
    <MemoryRouter>
      <ReportingPage />
    </MemoryRouter>,
  );
}

describe('ReportingPage', () => {
  beforeEach(() => {
    invokeMock.mockReset();
  });

  it('shows the export result path after a successful export', async () => {
    setUpInvoke({ cancelled: false, filePath: 'C:\\reports\\wec.html', openError: null });

    renderPage();
    await userEvent.click(await screen.findByRole('button', { name: 'Export HTML' }));

    expect(await screen.findByText('C:\\reports\\wec.html')).toBeDefined();
    expect(invokeMock).toHaveBeenCalledWith(
      'reporting',
      'exportHtml',
      { openAfterExport: true, host: null },
    );
  });

  it('treats a cancelled save dialog as a neutral outcome, not an error', async () => {
    setUpInvoke({ cancelled: true, filePath: null, openError: null });

    renderPage();
    await userEvent.click(await screen.findByRole('button', { name: 'Export JSON' }));

    expect(await screen.findByText('Export cancelled.')).toBeDefined();
  });

  it('keeps report-load diagnostics behind actionable guidance', async () => {
    invokeMock.mockImplementation((module: string, action: string) =>
      module === 'reporting' && action === 'getOverview'
        ? Promise.reject(new Error('raw report overview failure'))
        : Promise.resolve({}));

    renderPage();

    const alert = await screen.findByRole('alert');
    expect(within(alert).getByText('The report overview could not be loaded.')).toBeDefined();
    expect(within(alert).getByText('Cause')).toBeDefined();
    expect(within(alert).getByText('Next action')).toBeDefined();
    const details = within(alert).getByText('Technical details').closest('details') as HTMLDetailsElement;
    expect(details.open).toBe(false);
    await userEvent.click(details.querySelector('summary')!);
    expect(within(alert).getByText(/raw report overview failure/)).toBeDefined();
  });

  it('keeps export diagnostics local and collapsed', async () => {
    invokeMock.mockImplementation((_module: string, action: string) => {
      return action === 'getOverview'
        ? Promise.resolve(overviewWithData)
        : Promise.reject(new Error('raw report export failure'));
    });

    renderPage();
    await userEvent.click(await screen.findByRole('button', { name: 'Export HTML' }));

    const alert = await screen.findByRole('alert');
    expect(within(alert).getByText('The report could not be exported.')).toBeDefined();
    expect(within(alert).getByText('Next action')).toBeDefined();
    const details = within(alert).getByText('Technical details').closest('details') as HTMLDetailsElement;
    expect(details.open).toBe(false);
    await userEvent.click(details.querySelector('summary')!);
    expect(within(alert).getByText(/raw report export failure/)).toBeDefined();
  });

  it('keeps a post-export open failure separate from the successful save', async () => {
    setUpInvoke({
      cancelled: false,
      filePath: 'C:\\reports\\wec.html',
      openError: 'raw report open failure',
    });

    renderPage();
    await userEvent.click(await screen.findByRole('button', { name: 'Export HTML' }));

    expect(await screen.findByText('C:\\reports\\wec.html')).toBeDefined();
    expect(screen.getByRole('heading', { name: 'Report saved, but opening failed' })).toBeDefined();
    const alert = screen.getByRole('alert');
    expect(within(alert).getByText('The saved report could not be opened.')).toBeDefined();
    expect(within(alert).getByText(/Open C:\\reports\\wec.html manually/)).toBeDefined();
    const details = within(alert).getByText('Technical details').closest('details') as HTMLDetailsElement;
    expect(details.open).toBe(false);
  });

  it('shows incomplete security coverage in the report overview', async () => {
    setUpInvoke({ cancelled: true, filePath: null, openError: null });

    renderPage();

    expect(screen.getByRole('heading', { name: 'Report export' })).toBeDefined();
    expect(await screen.findByText(/12\/13 applicable checks evaluated — incomplete/)).toBeDefined();
    const aggregateContext = screen.getByText('One or more report sources are missing, stale, or incomplete.');
    expect(aggregateContext.previousElementSibling?.textContent).toBe('Partial');
    expect(aggregateContext.previousElementSibling?.className).toContain('border-warn-700');
    expect(screen.queryByText('REFRESH REQUIRED')).toBeNull();
    expect(screen.getByText('Persisted WMI/CIM inventory snapshot')).toBeDefined();
    expect(screen.getByText('5m old')).toBeDefined();
    expect(screen.getByText(/outside the report read contract/)).toBeDefined();
    expect(screen.getByText(/does not run checks or include the latest saved health snapshot/)).toBeDefined();
    expect(screen.queryByRole('link', { name: 'Open Inventory' })).toBeNull();
    expect(screen.getByRole('link', { name: 'Open Security' }).getAttribute('href'))
      .toBe('/clients/WEC-HOST?section=security');
  });

  it('presents a fully ready report aggregate as available', async () => {
    invokeMock.mockResolvedValue({
      ...overviewWithData,
      readiness: {
        ...overviewWithData.readiness,
        isReady: true,
        sources: overviewWithData.readiness.sources.map((source) => ({
          ...source,
          state: 'READY',
          isComplete: true,
        })),
      },
    } satisfies ReportOverview);

    renderPage();

    expect((await screen.findAllByText('Available'))[0].className).toContain('border-ok-700');
    expect(screen.getByText('All report sources are current and complete.')).toBeDefined();
    expect(screen.queryByText('READY')).toBeNull();
  });

  it('separates report source freshness, availability and execution status', async () => {
    invokeMock.mockResolvedValue({
      ...overviewWithData,
      readiness: {
        ...overviewWithData.readiness,
        sources: [
          { ...overviewWithData.readiness.sources[0], source: 'Fresh source', state: 'READY' },
          { ...overviewWithData.readiness.sources[0], source: 'Missing source', state: 'MISSING' },
          { ...overviewWithData.readiness.sources[0], source: 'Stale source', state: 'STALE' },
          { ...overviewWithData.readiness.sources[0], source: 'Partial source', state: 'INCOMPLETE' },
          { ...overviewWithData.readiness.sources[0], source: 'Future source', state: 'FUTURE_STATE' },
        ],
      },
    } satisfies ReportOverview);

    renderPage();

    expect(await screen.findByText('Fresh')).toBeDefined();
    expect(screen.getByText('Missing').className).toContain('border-warn-700');
    expect(screen.getByText('Stale')).toBeDefined();
    const partialSource = screen.getByText('Partial source').closest('li') as HTMLLIElement;
    expect(within(partialSource).getByText('Partial')).toBeDefined();
    expect(screen.getByText('Unknown').className).toContain('border-slate-700');
    expect(screen.queryByText(/^(READY|MISSING|STALE|INCOMPLETE|FUTURE_STATE)$/)).toBeNull();
  });

  it('disables exports when there is nothing to export yet', async () => {
    const missingOverview = {
      subjectHost: 'WEC-HOST',
      inventoryCapturedAtUtc: null,
      securityScanCompletedAtUtc: null,
      securityScanStatus: null,
      securityFindingCount: null,
      securityCoverage: null,
      readiness: {
        evaluatedAtUtc: '2026-07-02T18:10:00Z',
        isReady: false,
        sources: [
          {
            source: 'Hardware inventory',
            provenance: 'Persisted WMI/CIM inventory snapshot',
            state: 'MISSING',
            capturedAtUtc: null,
            ageSeconds: null,
            isComplete: false,
            summary: 'No hardware inventory data is available.',
          },
          {
            source: 'Security posture',
            provenance: 'Persisted Security scan and per-check outcomes',
            state: 'MISSING',
            capturedAtUtc: null,
            ageSeconds: null,
            isComplete: false,
            summary: 'No security posture data is available.',
          },
        ],
      },
    } satisfies ReportOverview;
    invokeMock.mockResolvedValue(missingOverview);

    renderPage();

    const exportButton = await screen.findByRole('button', { name: 'Export HTML' });
    expect(exportButton.hasAttribute('disabled')).toBe(true);
    expect(screen.getByRole('link', { name: 'Open Inventory' }).getAttribute('href'))
      .toBe('/clients/WEC-HOST?section=inventory');
    expect(screen.getByRole('link', { name: 'Open Security' }).getAttribute('href'))
      .toBe('/clients/WEC-HOST?section=security');
    expect(invokeMock.mock.calls.some((call) => call[1] === 'runScan')).toBe(false);
  });
});
