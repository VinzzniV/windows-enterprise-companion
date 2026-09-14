import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Microsoft365Data, Microsoft365Snapshot, Microsoft365Status, Microsoft365User } from '../../shared/api-types.generated';
import { BridgeInvokeError } from '../../shared/bridge/bridgeClient';
import { Microsoft365Page } from './Microsoft365Page';
import { Microsoft365DataView } from './Microsoft365DataView';
import { Microsoft365ContextPanel } from './Microsoft365ContextPanel';

const { requestMock, cancelMock } = vi.hoisted(() => ({ requestMock: vi.fn(), cancelMock: vi.fn() }));
vi.mock('../../shared/bridge/bridgeClient', async importOriginal => {
  const actual = await importOriginal<typeof import('../../shared/bridge/bridgeClient')>();
  return { ...actual, invokeCancellable: (...args: unknown[]) => ({ promise: requestMock(...args), cancel: cancelMock, requestId: 'fixture' }) };
});

const emptyData: Microsoft365Data = { tenants: [], users: [], groups: [], devices: [], managedDevices: [], licenses: [], members: [], activity: null, totalCount: null, truncated: false };
const user: Microsoft365User = { id: '11111111-1111-1111-1111-111111111111', displayName: 'Alex Example', userPrincipalName: 'alex@example.test', mail: null,
  accountEnabled: null, userType: null, department: null, jobTitle: null, officeLocation: null, createdAtUtc: null,
  onPremisesSid: null, onPremisesImmutableId: null, assignedLicenses: null };
const status: Microsoft365Status = { connection: { configuration: { tenantId: '', clientId: '' }, connected: false, account: null, permissions: [] }, sources: [], sessionRevision: 0, revision: 0, queries: [] };
const snapshot = (data: Partial<Microsoft365Data> = {}, resource: Microsoft365Snapshot['query']['resource'] = 'USERS'): Microsoft365Snapshot => ({
  query: { resource, objectId: null }, data: { ...emptyData, ...data }, updatedAtUtc: '2026-09-14T10:00:00Z', stale: false, refreshError: null, licenseCapacity: [], state: null,
});
const renderView = (value: Microsoft365Snapshot) => render(<MemoryRouter><Microsoft365DataView snapshot={value} /></MemoryRouter>);

describe('Microsoft 365 UI', () => {
  beforeEach(() => { requestMock.mockReset(); cancelMock.mockReset(); requestMock.mockResolvedValue(status); });

  it('opens disconnected without querying Graph or claiming zero tenant counts', async () => {
    render(<MemoryRouter><Microsoft365Page /></MemoryRouter>);
    await waitFor(() => expect(requestMock).toHaveBeenCalledWith('microsoft365', 'getStatus', {}));
    expect(screen.getByText('Not connected')).toBeTruthy();
    expect(requestMock).toHaveBeenCalledTimes(1);
    expect(screen.queryByText('Graph count: 0')).toBeNull();
    expect((screen.getByLabelText('Include Intune read access') as HTMLInputElement).checked).toBe(false);
  });

  it('keeps sign-in explicit and sends only identifiers and selected feature flags', async () => {
    requestMock.mockImplementation(async (_module, action) => action === 'connect' ? { ...status.connection, connected: true } : action === 'read' ? snapshot({}, 'TENANT') : status);
    const actor = userEvent.setup();
    render(<MemoryRouter><Microsoft365Page /></MemoryRouter>);
    await waitFor(() => expect(requestMock).toHaveBeenCalledTimes(1));
    await actor.type(screen.getByLabelText('Tenant ID'), '11111111-1111-1111-1111-111111111111');
    await actor.type(screen.getByLabelText('Client ID'), '22222222-2222-2222-2222-222222222222');
    await actor.click(screen.getByLabelText('Include Intune read access'));
    await actor.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(requestMock).toHaveBeenCalledWith('microsoft365', 'connect', {
      tenantId: '11111111-1111-1111-1111-111111111111', clientId: '22222222-2222-2222-2222-222222222222', enableIntune: true,
    });
  });

  it('displays known permission failures in the visible query panel', async () => {
    requestMock.mockImplementation(async (_module, action) => {
      if (action === 'getStatus') return { ...status, connection: { ...status.connection, connected: true } };
      throw new BridgeInvokeError({ code: 'MICROSOFT365_PERMISSION_MISSING', message: 'Intune requires DeviceManagementManagedDevices.Read.All.' });
    });
    render(<MemoryRouter initialEntries={['/microsoft365?resource=MANAGED_DEVICES']}><Microsoft365Page /></MemoryRouter>);
    expect((await screen.findByRole('alert')).textContent).toContain('Intune requires DeviceManagementManagedDevices.Read.All.');
    expect(screen.queryByText('No matching records in the loaded data.')).toBeNull();
  });

  it('shows loading and cancels requests on navigation teardown', async () => {
    requestMock.mockReturnValue(new Promise(() => {}));
    const rendered = render(<MemoryRouter><Microsoft365ContextPanel host="PC-1" /></MemoryRouter>);
    expect(await screen.findByText('Reading cached Microsoft 365 context…')).toBeTruthy();
    rendered.unmount();
    expect(cancelMock).toHaveBeenCalled();
  });

  it('preserves unknown account and license fields instead of reporting disabled or unlicensed', () => {
    renderView(snapshot({ users: [user] }));
    expect(screen.getAllByText('Not available').length).toBeGreaterThan(1);
    expect(screen.queryByText('None')).toBeNull();
    expect(screen.getByRole('link', { name: 'Alex Example' }).getAttribute('href')).toContain('resource=USER');
  });

  it('distinguishes no license and multiple licenses', () => {
    renderView(snapshot({ users: [{ ...user, assignedLicenses: [] }, { ...user, id: 'second', displayName: 'Other', assignedLicenses: [{ skuId: 'one', disabledPlans: [] }, { skuId: 'two', disabledPlans: [] }] }] }));
    expect(screen.getByText('None')).toBeTruthy();
    expect(screen.getByText('2 (multiple)')).toBeTruthy();
  });

  it('keeps stale data visible with the concrete refresh error and partial coverage', () => {
    const value = snapshot({ users: [user], truncated: true });
    renderView({ ...value, stale: true, refreshError: { code: 'MICROSOFT365_THROTTLED', message: 'Retry later.', details: null, requiredPrivilege: null } });
    expect(screen.getByRole('alert').textContent).toContain('Retry later');
    expect(screen.getByText(/Partial inventory/)).toBeTruthy();
    expect(screen.getByText(/Stale snapshot/)).toBeTruthy();
    expect(screen.getByText('Alex Example')).toBeTruthy();
  });

  it('paginates and filters the whole loaded collection', async () => {
    const actor = userEvent.setup();
    renderView(snapshot({ users: Array.from({ length: 60 }, (_, index) => ({ ...user, id: String(index), displayName: `User ${String(index).padStart(2, '0')}` })) }));
    expect(screen.getByText('Page 1 of 2')).toBeTruthy();
    expect(screen.queryByText('User 59')).toBeNull();
    await actor.click(screen.getByRole('button', { name: /next/i }));
    expect(screen.getByText('User 59')).toBeTruthy();
    await actor.type(screen.getByLabelText('Search loaded data'), 'User 01');
    expect(screen.getByText('User 01')).toBeTruthy();
    expect(screen.getByText('Page 1 of 1')).toBeTruthy();
  });

  it('offers user group, device and license navigation within WEC', () => {
    renderView(snapshot({ users: [user] }, 'USER'));
    expect(screen.getByRole('link', { name: 'Direct groups' }).getAttribute('href')).toContain('USER_GROUPS');
    expect(screen.getByRole('link', { name: 'Licenses and service plans' }).getAttribute('href')).toContain('USER_LICENSES');
    expect(screen.getByRole('link', { name: 'Registered devices' }).getAttribute('href')).toContain('USER_DEVICES');
  });

  it('shows license capacity warnings and disabled plans without invented product names', async () => {
    const actor = userEvent.setup();
    const value = snapshot({ licenses: [{ id: 'license', skuId: 'sku', skuPartNumber: 'DYNAMIC_SKU', capabilityStatus: 'Enabled', appliesTo: 'User', enabledSeats: 100, consumedSeats: 99,
      servicePlans: [{ id: 'plan', name: 'DYNAMIC_PLAN', status: 'Disabled' }] }] }, 'LICENSES');
    renderView({ ...value, licenseCapacity: [{ skuId: 'sku', remainingSeats: 1, nearlyExhausted: true, overAssigned: false }] });
    expect(screen.getByText('Nearly exhausted')).toBeTruthy();
    await actor.click(screen.getByText('1 service plans'));
    expect(screen.getByText('Disabled')).toBeTruthy();
    expect(screen.getByText(/DYNAMIC_PLAN/)).toBeTruthy();
  });

  it('loads context only from the cache and labels name matches as candidates', async () => {
    requestMock.mockResolvedValue({ state: 'Candidate', explanation: 'Name only; not a confirmed relationship.', user: null, device: null, managedDevice: null, observedAtUtc: null, stale: true });
    render(<MemoryRouter><Microsoft365ContextPanel host="PC-1" /></MemoryRouter>);
    expect(await screen.findByText('Candidate')).toBeTruthy();
    expect(requestMock).toHaveBeenCalledWith('microsoft365', 'getContext', { host: 'PC-1', sid: undefined, userPrincipalName: undefined, entraDeviceId: undefined });
    expect(requestMock).toHaveBeenCalledTimes(1);
    expect(screen.getByText(/not established/)).toBeTruthy();
  });
});
