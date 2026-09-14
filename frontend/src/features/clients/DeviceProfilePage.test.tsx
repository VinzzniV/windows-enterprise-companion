import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useNavigate } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { DeviceProfileResult, Microsoft365ReadState, ObjectSource } from '../../shared/api-types.generated';
import { objectPath } from '../../shared/objects/objectRoutes';
import { DeviceProfilePage } from './DeviceProfilePage';

const mocks = vi.hoisted(() => ({ invoke: vi.fn(), request: { activeDirectory: {}, kaspersky: null }, refresh: vi.fn(), cancel: vi.fn() }));
vi.mock('../../shared/bridge/bridgeClient', () => ({ invokeCancellable: mocks.invoke }));
vi.mock('../../shared/environment/EnvironmentContext', () => ({
  useEnvironmentRequest: () => mocks.request,
  useEnvironment: () => ({ refresh: mocks.refresh, loading: false, cancel: mocks.cancel }),
}));

const tenant = '11111111-1111-1111-1111-111111111111';
const id = '22222222-2222-2222-2222-222222222222';
const secondId = '33333333-3333-3333-3333-333333333333';

function state(resource: Microsoft365ReadState['query']['resource'], objectId: string | null = null): Microsoft365ReadState {
  const now = Date.now();
  return { query: { resource, objectId }, tenantId: tenant, sessionRevision: 1, snapshotRevision: 1, availability: 'AVAILABLE',
    loading: false, retrievedAtUtc: new Date(now).toISOString(), lastAttemptAtUtc: new Date(now).toISOString(), lastAttemptError: null,
    retainedUntilUtc: new Date(now + 3600_000).toISOString(), freshUntilUtc: new Date(now + 600_000).toISOString(), freshness: 'FRESH', coverage: 'RETURNED_SET', loadedCount: 1, declaredTotal: null };
}
function profile(source: ObjectSource = 'ENTRA'): DeviceProfileResult {
  return {
    reference: { kind: 'DEVICE', source, scope: tenant, id }, title: 'Cloud device', identity: 'SCOPED_ID', explanation: 'Scoped source identity.', operationalHost: null,
    wec: null, directory: null, directoryRecords: [], managementCandidates: null, relationships: [], candidates: [], sourceErrors: [],
    cloud: { tenantId: tenant, sessionRevision: 1, revision: 1,
      entraReads: [{ state: state('DEVICE', id), devices: [{ id, deviceId: secondId, displayName: 'Cloud device', operatingSystem: null, operatingSystemVersion: null, trustType: null, accountEnabled: false, approximateLastSignInAtUtc: null }] }],
      intune: { state: { ...state('MANAGED_DEVICES'), availability: 'NOT_ENABLED', loadedCount: null }, devices: [] },
      registeredOwners: { state: state('DEVICE_OWNERS', id), members: [] }, managedDetails: [],
    },
  };
}
function NavigateToSecond() { const navigate = useNavigate(); return <button onClick={() => navigate(`/devices/entra/${tenant}/${secondId}`)}>Second device</button>; }
function renderPage(value = profile()) {
  return render(<MemoryRouter initialEntries={[objectPath(value.reference)]}><NavigateToSecond /><Routes><Route path="/devices/:source/:scope/:objectId" element={<DeviceProfilePage />} /></Routes></MemoryRouter>);
}
beforeEach(() => {
  mocks.invoke.mockReset(); mocks.refresh.mockReset(); mocks.cancel.mockReset();
  mocks.invoke.mockImplementation(() => ({ promise: Promise.resolve(profile()), cancel: mocks.cancel }));
});
afterEach(() => { vi.useRealTimers(); });

describe('device profiles', () => {
  it('opens cloud-only evidence without source reads or a Windows action fallback', async () => {
    renderPage();
    expect(await screen.findByRole('heading', { name: 'Cloud device' })).toBeTruthy();
    expect(mocks.invoke).toHaveBeenCalledTimes(1);
    expect(mocks.invoke).toHaveBeenCalledWith('clients', 'getProfile', expect.objectContaining({ loadDirectoryIdentity: false }));
    expect(screen.queryByRole('link', { name: 'Inventory' })).toBeNull();
    expect(screen.getByText('No')).toBeTruthy();
    expect(screen.getAllByText('Not available').length).toBeGreaterThan(0);
    expect((screen.getByRole('button', { name: 'Load bounded Intune inventory' }) as HTMLButtonElement).disabled).toBe(true);
  });

  it('refreshes the selected Entra query with its tenant and then recomposes cached facts', async () => {
    renderPage();
    await screen.findByRole('heading', { name: 'Cloud device' });
    fireEvent.click(screen.getByRole('button', { name: 'Load this Entra source' }));
    await waitFor(() => expect(mocks.invoke).toHaveBeenCalledTimes(3));
    expect(mocks.invoke.mock.calls[1]).toEqual(['microsoft365', 'read', { resource: 'DEVICE', objectId: id, tenantId: tenant, refresh: true }]);
    expect(mocks.invoke.mock.calls[2][0]).toBe('clients');
  });

  it('preserves source failure and partial coverage alongside older facts', async () => {
    const value = profile();
    value.cloud!.entraReads[0].state = { ...state('DEVICE', id), freshness: 'STALE', coverage: 'PARTIAL', declaredTotal: 50, lastAttemptError: { code: 'MICROSOFT365_THROTTLED', message: 'Retry later', details: null, requiredPrivilege: null } };
    mocks.invoke.mockReturnValue({ promise: Promise.resolve(value), cancel: mocks.cancel });
    renderPage(value);
    expect(await screen.findByRole('heading', { name: 'Cloud device' })).toBeTruthy();
    expect(screen.getByText(/Partial source query/).textContent).toContain('50');
    expect(screen.getByRole('alert').textContent).toContain('Retry later');
  });

  it('renders each source record and navigates scoped user relationships', async () => {
    const value = profile();
    value.cloud!.entraReads[0].devices.push({ ...value.cloud!.entraReads[0].devices[0], displayName: 'Second returned record' });
    value.identity = 'AMBIGUOUS';
    value.relationships.push({ target: { kind: 'USER', source: 'ENTRA', scope: tenant, id: secondId }, label: 'Associated account', relation: 'Associated user (Intune)', evidence: 'SCOPED_ID', explanation: 'Not a primary-user claim.' });
    mocks.invoke.mockReturnValue({ promise: Promise.resolve(value), cancel: mocks.cancel });
    renderPage(value);
    expect(await screen.findByText('Second returned record')).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Associated account' }).getAttribute('href')).toBe(`/users/entra/${tenant}/${secondId}`);
  });

  it('clears the old subject immediately and ignores its late response', async () => {
    let complete: (value: DeviceProfileResult) => void = () => {};
    mocks.invoke.mockReturnValueOnce({ promise: new Promise<DeviceProfileResult>(resolve => { complete = resolve; }), cancel: mocks.cancel });
    const next = { ...profile(), reference: { ...profile().reference, id: secondId }, title: 'Second profile' };
    mocks.invoke.mockReturnValue({ promise: Promise.resolve(next), cancel: mocks.cancel });
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: 'Second device' }));
    await screen.findByRole('heading', { name: 'Second profile' });
    await act(async () => { complete(profile()); });
    expect(screen.queryByRole('heading', { name: 'Cloud device' })).toBeNull();
    expect(mocks.cancel).toHaveBeenCalled();
  });

  it('updates freshness and removes expired data through a cache-only reload', async () => {
    vi.useFakeTimers();
    const value = profile();
    value.cloud!.entraReads[0].state.freshUntilUtc = new Date(Date.now() + 1000).toISOString();
    value.cloud!.entraReads[0].state.retainedUntilUtc = new Date(Date.now() + 2000).toISOString();
    mocks.invoke.mockReturnValueOnce({ promise: Promise.resolve(value), cancel: mocks.cancel });
    mocks.invoke.mockReturnValue({ promise: Promise.resolve({ ...value, cloud: null, title: 'Expired profile' }), cancel: mocks.cancel });
    renderPage(value);
    await act(async () => {});
    await act(async () => { await vi.advanceTimersByTimeAsync(1100); });
    expect(screen.getByText('Available · Stale')).toBeTruthy();
    await act(async () => { await vi.advanceTimersByTimeAsync(1100); });
    expect(screen.queryByText('Entra object read')).toBeNull();
    expect(mocks.invoke.mock.calls.every(call => call[0] === 'clients')).toBe(true);
  });
});
