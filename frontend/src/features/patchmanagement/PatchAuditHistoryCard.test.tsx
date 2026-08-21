import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { PatchAuditEntry } from '../../shared/api-types';
import { PatchAuditHistoryCard } from './PatchAuditHistoryCard';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../shared/bridge/bridgeClient', async (importOriginal) => {
  const original = await importOriginal<typeof import('../../shared/bridge/bridgeClient')>();
  return { ...original, invoke: invokeMock };
});

const baseEntry: Omit<PatchAuditEntry, 'id' | 'action' | 'result'> = {
  timestampUtc: '2026-07-03T11:00:00Z',
  userName: 'vinz',
  productId: 'firefox',
  depotId: 'depot-a',
  oldVersion: null,
  newVersion: '2.0',
  targetClients: ['pc1.kauth.local', 'pc2.example.local'],
  previewJson: null,
  errorMessage: null,
};

const entries: PatchAuditEntry[] = [
  { ...baseEntry, id: 1, action: 'PREPARE_PACKAGES', result: 'PLANNED' },
  { ...baseEntry, id: 2, action: 'ROLLOUT_REQUESTED', result: 'SUCCESS' },
  { ...baseEntry, id: 3, action: 'PACKAGE_UPDATE', result: 'FAILED', errorMessage: 'opsi failed' },
];

describe('PatchAuditHistoryCard', () => {
  beforeEach(() => {
    invokeMock.mockReset();
    invokeMock.mockResolvedValue({ entries });
  });

  it('owns the current history request and presents its persisted evidence', async () => {
    render(<PatchAuditHistoryCard />);

    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith(
      'patchmanagement',
      'getAuditLog',
      {},
    ));
    const previewRow = (await screen.findByText('PREPARE_PACKAGES')).closest('tr')!;
    expect(within(previewRow).getByText('Succeeded')).toBeDefined();
    expect(within(previewRow).getByText('Preview created')).toBeDefined();
    expect(within(previewRow).getByText('missing → 2.0')).toBeDefined();
    expect(within(previewRow).getByText('2: pc1, pc2.example.local')).toBeDefined();
    expect(within(previewRow).getByText(new Date(baseEntry.timestampUtc).toLocaleString())).toBeDefined();

    const successRow = screen.getByText('ROLLOUT_REQUESTED').closest('tr')!;
    expect(within(successRow).getByText('Succeeded')).toBeDefined();
    const failureRow = screen.getByText('PACKAGE_UPDATE').closest('tr')!;
    expect(within(failureRow).getByText('Failed')).toBeDefined();
    expect(within(failureRow).getByText('opsi failed')).toBeDefined();
    expect(screen.queryByText(/^(SUCCESS|FAILED|PLANNED)$/)).toBeNull();
  });

  it('keeps a failed read distinct from an empty history and retries locally', async () => {
    invokeMock.mockRejectedValueOnce(new Error('Audit database is locked'));

    render(<PatchAuditHistoryCard />);

    expect(await screen.findByText('The patch history could not be loaded.')).toBeDefined();
    expect(screen.queryByText('No Patch Management actions have been recorded yet.')).toBeNull();
    await userEvent.click(screen.getByRole('button', { name: 'Reload history' }));

    expect(await screen.findByText('PREPARE_PACKAGES')).toBeDefined();
    expect(invokeMock).toHaveBeenCalledTimes(2);
  });
});
