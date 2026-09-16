import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Microsoft365Snapshot } from '../../shared/api-types.generated';
import { LicenseWorkspace } from './LicenseWorkspace';

const mocks = vi.hoisted(() => ({ request: vi.fn(), refresh: vi.fn(), generation: 0 }));
vi.mock('../../shared/bridge/bridgeClient', () => ({ invokeCancellable: (_module: string, action: string, payload: unknown) =>
  ({ promise: mocks.request(action, payload), cancel: vi.fn() }), BridgeInvokeError: class extends Error {} }));
vi.mock('../../shared/objects/WorkingSetContext', () => ({ useOptionalWorkingSet: () => ({ refreshCached: mocks.refresh }), useWorkingSetSessions: () => ({ cloud: mocks.generation }) }));
const tenant = '11111111-1111-1111-1111-111111111111';
function catalogue(): Microsoft365Snapshot {
  return { query: { resource: 'LICENSES', objectId: null, securityIdentifier: null }, updatedAtUtc: new Date().toISOString(), stale: false, refreshError: null,
    licenseCapacity: [], data: { tenants: [], users: [], groups: [], devices: [], managedDevices: [], licenses: [], members: [], activity: null, totalCount: null, truncated: false },
    state: { query: { resource: 'LICENSES', objectId: null, securityIdentifier: null }, tenantId: tenant, sessionRevision: 1, snapshotRevision: 1,
      availability: 'AVAILABLE', loading: false, retrievedAtUtc: new Date().toISOString(), lastAttemptAtUtc: null, lastAttemptError: null,
      retainedUntilUtc: new Date(Date.now() + 3600_000).toISOString(), freshUntilUtc: null, freshness: 'FRESH', coverage: 'RETURNED_SET', loadedCount: 0, declaredTotal: null } };
}
describe('license workspace', () => {
  beforeEach(() => { mocks.request.mockReset(); mocks.refresh.mockReset(); mocks.generation = 0; mocks.request.mockResolvedValue(catalogue()); });
  it('opens cache-only and preserves the selected SKU and tenant in reverse navigation', async () => {
    render(<MemoryRouter initialEntries={[`/software?section=licenses&sku=sku-one&tenant=${tenant}`]}><LicenseWorkspace /></MemoryRouter>);
    await waitFor(() => expect(mocks.request).toHaveBeenCalledWith('read', { resource: 'LICENSES', tenantId: tenant, refresh: false, cacheOnly: true }));
    expect((await screen.findByRole('link', { name: 'Review loaded user assignments for this SKU' })).getAttribute('href')).toBe(`/users/workspace?source=ENTRA&sku=sku-one&tenant=${tenant}`);
    fireEvent.click(screen.getByRole('button', { name: 'Load tenant licenses' }));
    await waitFor(() => expect(mocks.request).toHaveBeenLastCalledWith('read', { resource: 'LICENSES', tenantId: tenant, refresh: true, cacheOnly: false }));
    await waitFor(() => expect(mocks.refresh).toHaveBeenCalledTimes(1));
  });
  it('does not render expired catalogue evidence', async () => {
    const expired = catalogue(); expired.state!.retainedUntilUtc = new Date(Date.now() - 1000).toISOString();
    mocks.request.mockResolvedValue(expired);
    render(<MemoryRouter><LicenseWorkspace /></MemoryRouter>);
    expect(await screen.findByText(/No retained license catalogue/)).toBeTruthy();
    expect(screen.queryByText(/Graph supplies SKU/)).toBeNull();
  });
});
