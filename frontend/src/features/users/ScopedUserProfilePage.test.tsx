import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useNavigate } from 'react-router-dom';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import type { Microsoft365ReadState, ScopedUserProfile, UserProfileResult } from '../../shared/api-types.generated';
import { objectPath } from '../../shared/objects/objectRoutes';
import { ScopedUserProfilePage } from './ScopedUserProfilePage';

const mocks = vi.hoisted(() => ({ invoke: vi.fn(), cancel: vi.fn(), export: vi.fn() }));
vi.mock('../../shared/bridge/bridgeClient', async (original) => ({
  ...await original<typeof import('../../shared/bridge/bridgeClient')>(), invokeCancellable: mocks.invoke, invoke: mocks.export,
}));
vi.mock('../../shared/targets/TargetContext', () => ({ useTargets: () => ({ adminCredentials: null }) }));
vi.mock('../../shared/viewCache', () => ({ loadView: () => null }));
const tenant = '11111111-1111-1111-1111-111111111111';
const id = '22222222-2222-2222-2222-222222222222';
const other = '33333333-3333-3333-3333-333333333333';
const sid = 'S-1-5-21-1-2-3-1001';
function state(resource: Microsoft365ReadState['query']['resource']): Microsoft365ReadState {
  return { query: { resource, objectId: id, securityIdentifier: null }, tenantId: tenant, sessionRevision: 1, snapshotRevision: 1,
    availability: 'AVAILABLE', loading: false, retrievedAtUtc: new Date().toISOString(), lastAttemptAtUtc: new Date().toISOString(), lastAttemptError: null,
    retainedUntilUtc: new Date(Date.now() + 3600_000).toISOString(), freshUntilUtc: new Date(Date.now() + 600_000).toISOString(),
    freshness: 'FRESH', coverage: 'RETURNED_SET', loadedCount: 1, declaredTotal: null };
}
function profile(): ScopedUserProfile {
  const user = { id, displayName: 'Cloud account', userPrincipalName: 'cloud@example.test', mail: null, accountEnabled: null, userType: 'Guest',
    department: null, jobTitle: null, officeLocation: null, createdAtUtc: null, onPremisesSid: null, onPremisesImmutableId: null, assignedLicenses: null };
  return { reference: { kind: 'USER', source: 'ENTRA', scope: tenant, id }, title: 'Cloud account', identity: 'SCOPED_ID', explanation: 'One tenant-scoped account.',
    directory: null, adProfile: null, entraUser: user, relationships: [], candidates: [], sourceErrors: [],
    cloud: { tenantId: tenant, sessionRevision: 1, revision: 1, userReads: [{ state: state('USER'), users: [user] }],
      tenantLicenses: { state: { ...state('LICENSES'), availability: 'NOT_CACHED', loadedCount: null }, licenses: [] },
      userLicenses: { state: { ...state('USER_LICENSES'), availability: 'NOT_CACHED', loadedCount: null }, licenses: [] },
      directGroups: { state: state('USER_GROUPS'), groups: [] }, registeredDevices: null, associatedIntune: [],
      signIn: { state: { ...state('USER_ACTIVITY'), availability: 'NOT_ENABLED', loadedCount: null }, activity: null }, registration: null,
    } };
}
function adProfile(): UserProfileResult {
  return { identity: { objectId: other, sid, displayName: 'AD account', samAccountName: 'ad', userPrincipalName: 'ad@example.test', mail: null,
    employeeId: null, department: null, title: null, managerDistinguishedName: null, distinguishedName: 'CN=ad,DC=example,DC=test', organizationalUnitPath: 'DC=example,DC=test' },
    lifecycle: { enabled: true, createdAtUtc: null, accountExpiresAtUtc: null, replicatedLastLogonAtUtc: null, passwordLastSetAtUtc: null, passwordExpiresAtUtc: null, passwordNeverExpires: null },
    access: { directGroups: [], privilegedCoverage: 'NOT_EVALUATED', privilegedCoverageExplanation: 'Not evaluated', directPrivilegedGroups: [] },
    devices: { coverage: 'NOT_CAPTURED', explanation: 'No stored evidence', sourceCoverage: { evaluatedDeviceCount: 0, workingSetTruncated: false, multipleLatestSnapshotDeviceCount: 0, storedDeviceCount: 0, evidenceCapturedDeviceCount: 0, notCapturedDeviceCount: 0, unavailableDeviceCount: 0, truncatedDeviceCount: 0 }, totalLinkedDeviceCount: 0, linkedDevicesTruncated: false, linkedDevices: [] } };
}
function Navigation() { const navigate = useNavigate(); return <button onClick={() => navigate(`/users/entra/${tenant}/${other}`)}>Other account</button>; }
function page() {
  render(<MemoryRouter initialEntries={[objectPath(profile().reference)]}><Navigation /><Routes>
    <Route path="/users/:source/:scope/:objectId" element={<ScopedUserProfilePage />} />
  </Routes></MemoryRouter>);
}
beforeEach(() => {
  mocks.invoke.mockReset(); mocks.cancel.mockReset(); mocks.export.mockReset();
  mocks.invoke.mockReturnValue({ promise: Promise.resolve(profile()), cancel: mocks.cancel });
});
afterEach(() => vi.useRealTimers());

it('opens a guest with unknown values using only cached profile composition', async () => {
  page();
  await screen.findByRole('heading', { name: 'Cloud account' });
  expect(mocks.invoke).toHaveBeenCalledTimes(1);
  expect(mocks.invoke).toHaveBeenCalledWith('usermanagement', 'getProfile', expect.objectContaining({ loadDirectoryIdentity: false }));
  expect(screen.getByText('Guest')).toBeTruthy();
  expect(screen.queryByRole('button', { name: 'Export Markdown checklist' })).toBeNull();
  expect((screen.getByRole('button', { name: 'Resolve AD account by exact SID' }) as HTMLButtonElement).disabled).toBe(true);
});

it('loads one explicit user source with its tenant then recomposes without loading adjacent sources', async () => {
  page();
  fireEvent.click(await screen.findByRole('button', { name: 'Load Entra user object' }));
  await waitFor(() => expect(mocks.invoke).toHaveBeenCalledTimes(3));
  expect(mocks.invoke.mock.calls[1]).toEqual(['microsoft365', 'read', { resource: 'USER', objectId: id, securityIdentifier: null, tenantId: tenant, refresh: true }]);
  expect(mocks.invoke.mock.calls[2][1]).toBe('getProfile');
});

it('keeps unavailable licenses and partial group evidence independent and navigates the scoped group', async () => {
  const value = profile();
  value.cloud!.directGroups!.state.coverage = 'PARTIAL';
  value.relationships.push({ target: { kind: 'GROUP', source: 'ENTRA', scope: tenant, id: other }, label: 'Operations', relation: 'Direct group (Entra)', evidence: 'SCOPED_ID', explanation: 'Direct membership only' });
  mocks.invoke.mockReturnValue({ promise: Promise.resolve(value), cancel: mocks.cancel });
  page();
  fireEvent.click(await screen.findByRole('button', { name: 'Groups' }));
  expect(screen.getByRole('link', { name: 'Operations' }).getAttribute('href')).toBe(`/groups/entra/${tenant}/${other}`);
  expect(screen.getByText(/Partial source query/)).toBeTruthy();
  fireEvent.click(screen.getByRole('button', { name: 'Licenses' }));
  expect(screen.getByText('Assigned-license evidence is unavailable.')).toBeTruthy();
  expect(screen.queryByText(/No license rows returned/)).toBeNull();
  expect(mocks.invoke).toHaveBeenCalledTimes(1);
});

it('discards a late response from the previous account', async () => {
  let complete: (value: ScopedUserProfile) => void = () => {};
  mocks.invoke.mockReturnValueOnce({ promise: new Promise<ScopedUserProfile>(resolve => { complete = resolve; }), cancel: mocks.cancel });
  mocks.invoke.mockReturnValue({ promise: Promise.resolve({ ...profile(), title: 'Second account', reference: { ...profile().reference, id: other } }), cancel: mocks.cancel });
  page();
  fireEvent.click(screen.getByRole('button', { name: 'Other account' }));
  await screen.findByRole('heading', { name: 'Second account' });
  await act(async () => complete(profile()));
  expect(screen.queryByRole('heading', { name: 'Cloud account' })).toBeNull();
  expect(mocks.cancel).toHaveBeenCalled();
});

it('expires source data with a cache-only reload', async () => {
  vi.useFakeTimers();
  const value = profile();
  value.cloud!.userReads[0].state.retainedUntilUtc = new Date(Date.now() + 1000).toISOString();
  mocks.invoke.mockReturnValueOnce({ promise: Promise.resolve(value), cancel: mocks.cancel });
  mocks.invoke.mockReturnValue({ promise: Promise.resolve({ ...value, cloud: null, entraUser: null, title: 'Expired account' }), cancel: mocks.cancel });
  page();
  await act(async () => {});
  await act(async () => { await vi.advanceTimersByTimeAsync(1100); });
  expect(screen.queryByText('Guest')).toBeNull();
  expect(mocks.invoke.mock.calls.every(call => call[0] === 'usermanagement')).toBe(true);
});

it('keeps Leaver marks across tabs and excludes all cloud facts from its export', async () => {
  const value = profile(); value.adProfile = adProfile();
  mocks.invoke.mockReturnValue({ promise: Promise.resolve(value), cancel: mocks.cancel });
  mocks.export.mockResolvedValue({ cancelled: true, filePath: null });
  page();
  fireEvent.click(await screen.findByRole('button', { name: 'Leaver review' }));
  const first = screen.getAllByRole('checkbox')[0];
  fireEvent.click(first);
  fireEvent.click(screen.getByRole('button', { name: 'Groups' }));
  fireEvent.click(screen.getByRole('button', { name: 'Leaver review' }));
  expect((first as HTMLInputElement).checked).toBe(true);
  fireEvent.click(screen.getByRole('button', { name: 'Export Markdown checklist' }));
  await waitFor(() => expect(mocks.export).toHaveBeenCalledTimes(1));
  const markdown = mocks.export.mock.calls[0][2].markdown;
  expect(markdown).toContain('AD account');
  expect(markdown).not.toContain('cloud@example.test');
  expect(markdown).not.toContain(tenant);
  expect(markdown).toContain('Microsoft 365 accounts, licenses, groups and device relationships are excluded');
});
