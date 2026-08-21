import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { HygieneLoadProgress } from '../api-types';
import { HygieneLoadStatus } from './HygieneLoadStatus';

const progress: HygieneLoadProgress = {
  operationId: 'operation-1',
  phase: 'LOADING_SOURCES',
  startedAtUtc: '2026-08-19T12:00:00Z',
  completedSources: 2,
  totalSources: 4,
  partialDeviceCount: 125,
  partialSummary: {
    total: 125, adComputers: 120, kasperskyComputers: 100, opsiComputers: 0, nessusComputers: 0,
    healthy: 0, problems: 0, incomplete: 125, stale: 0, missingKaspersky: 0, orphanKaspersky: 0,
    missingOpsi: 0, orphanOpsi: 0, outdated: 0, missingNessus: 0, staleNessus: 0, nessusCritical: 0, nessusHigh: 0,
  },
  sources: [
    { source: 'ACTIVE_DIRECTORY', status: 'AVAILABLE', itemCount: 120, message: null },
    { source: 'KASPERSKY', status: 'PARTIAL', itemCount: 100, message: 'Result limit reached' },
    { source: 'OPSI', status: 'RUNNING', itemCount: null, message: null },
    { source: 'NESSUS', status: 'RUNNING', itemCount: null, message: null },
  ],
};

describe('HygieneLoadStatus', () => {
  it('shows source progress, partial devices, elapsed guidance and cancel', async () => {
    const cancel = vi.fn();
    render(<HygieneLoadStatus progress={progress} elapsedSeconds={12} onCancel={cancel} />);

    expect(screen.getByText(/2 of 4 sources complete/).textContent).toContain('125 devices available so far');
    expect(screen.getByRole('group', { name: 'Active Directory: Available · 120 items' })).toBeTruthy();
    expect(screen.getByRole('group', { name: 'Kaspersky: Partial · 100 items' })).toBeTruthy();
    expect(screen.getAllByText('Running')).toHaveLength(2);
    expect(screen.getByText(/take up to three minutes/)).toBeTruthy();
    await userEvent.click(screen.getByRole('button', { name: 'Cancel load' }));
    expect(cancel).toHaveBeenCalledOnce();
  });
});
