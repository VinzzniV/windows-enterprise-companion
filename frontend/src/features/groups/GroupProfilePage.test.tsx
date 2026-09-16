import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation, useNavigate } from 'react-router-dom';
import { beforeEach, expect, it, vi } from 'vitest';
import type { GroupProfileResult, Microsoft365ReadState } from '../../shared/api-types.generated';
import { objectPath } from '../../shared/objects/objectRoutes';
import { GroupProfilePage } from './GroupProfilePage';
import { ResolveGroupPage } from './ResolveGroupPage';

const mocks = vi.hoisted(() => ({ invoke: vi.fn(), cancel: vi.fn() }));
vi.mock('../../shared/bridge/bridgeClient', async (original) => ({ ...await original<typeof import('../../shared/bridge/bridgeClient')>(), invokeCancellable: mocks.invoke }));
vi.mock('../../shared/targets/TargetContext', () => ({ useTargets: () => ({ adminCredentials: null }) }));
vi.mock('../../shared/viewCache', () => ({ loadView: () => null }));
const tenant = '11111111-1111-1111-1111-111111111111';
const id = '22222222-2222-2222-2222-222222222222';
const other = '33333333-3333-3333-3333-333333333333';
function state(resource: Microsoft365ReadState['query']['resource']): Microsoft365ReadState {
  return { query: { resource, objectId: id, securityIdentifier: null }, tenantId: tenant, sessionRevision: 1, snapshotRevision: 1,
    availability: 'AVAILABLE', loading: false, retrievedAtUtc: new Date().toISOString(), lastAttemptAtUtc: new Date().toISOString(), lastAttemptError: null,
    retainedUntilUtc: new Date(Date.now() + 3600_000).toISOString(), freshUntilUtc: new Date(Date.now() + 600_000).toISOString(),
    freshness: 'FRESH', coverage: 'PARTIAL', loadedCount: 2, declaredTotal: null };
}
function profile(): GroupProfileResult {
  return { reference: { kind: 'GROUP', source: 'ENTRA', scope: tenant, id }, title: 'Cloud group', identity: 'SCOPED_ID', explanation: 'Direct source evidence',
    directory: null, directoryMembers: null, sourceErrors: [], relationships: [{ target: { kind: 'USER', source: 'ENTRA', scope: tenant, id: other }, label: other,
      relation: 'Direct member (Entra)', evidence: 'SCOPED_ID', explanation: 'Limited fields remain unknown.' }],
    cloud: { tenantId: tenant, sessionRevision: 1, revision: 1, groupReads: [{ state: state('GROUP'), groups: [{ id, displayName: 'Cloud group', securityEnabled: null, mailEnabled: null,
      groupTypes: null, membershipRule: null, membershipRuleProcessingState: null, visibility: null }] }], directMembers: { state: state('GROUP_MEMBERS'), members: [
        { id: other, displayName: null, objectType: 'user', userPrincipalName: null }, { id: null, displayName: null, objectType: null, userPrincipalName: null },
      ] } } };
}
function Navigation() { const navigate = useNavigate(); const location = useLocation(); return <><button onClick={() => navigate(`/groups/entra/${tenant}/${other}`)}>Other group</button><button onClick={() => navigate(-1)}>Back</button><output data-testid="route">{location.search}</output></>; }
function page(value = profile(), initial?: string) {
  render(<MemoryRouter initialEntries={[initial ?? objectPath(value.reference)]}><Navigation /><Routes>
    <Route path="/groups/:source/:scope/:objectId" element={<GroupProfilePage />} />
    <Route path="/groups/resolve" element={<ResolveGroupPage />} />
  </Routes></MemoryRouter>);
}
beforeEach(() => { mocks.invoke.mockReset(); mocks.cancel.mockReset(); mocks.invoke.mockReturnValue({ promise: Promise.resolve(profile()), cancel: mocks.cancel }); });

it('opens a cloud-only group from cached evidence and preserves limited member objects', async () => {
  page();
  await screen.findByRole('heading', { name: 'Cloud group' });
  expect(mocks.invoke).toHaveBeenCalledTimes(1);
  expect(mocks.invoke).toHaveBeenCalledWith('groups', 'getProfile', expect.objectContaining({ read: 'CACHED', connection: null }));
  expect(screen.getAllByText(/Limited-information object/)).toHaveLength(2);
  expect(screen.getByRole('link', { name: other }).getAttribute('href')).toBe(`/users/entra/${tenant}/${other}`);
  expect(screen.getAllByText(/Partial source query/)).toHaveLength(2);
  expect(screen.queryByRole('button', { name: 'Load this AD group by GUID' })).toBeNull();
});

it('refreshes only the selected scoped Graph membership query', async () => {
  page();
  await screen.findByRole('heading', { name: 'Cloud group' });
  fireEvent.click(screen.getAllByRole('button', { name: 'Load this source query' })[1]);
  await waitFor(() => expect(mocks.invoke).toHaveBeenCalledTimes(3));
  expect(mocks.invoke.mock.calls[1]).toEqual(['microsoft365', 'read', { resource: 'GROUP_MEMBERS', objectId: id, securityIdentifier: null, tenantId: tenant, refresh: true }]);
});

it('loads the requested AD member page explicitly and labels its source count', async () => {
  const value = profile(); value.reference = { ...value.reference, source: 'ACTIVE_DIRECTORY', scope: 'example.test' }; value.cloud = null;
  const sourceState = { retrievedAtUtc: new Date().toISOString(), lastAttemptAtUtc: new Date().toISOString(), lastAttemptError: null,
    sessionRevision: 1, revision: 1, retainedUntilUtc: new Date(Date.now() + 3600_000).toISOString(), freshUntilUtc: new Date(Date.now() + 600_000).toISOString(), stale: false };
  value.directory = { state: sourceState, data: null };
  value.directoryMembers = { state: sourceState, data: { directoryScope: 'example.test', groupObjectId: id, retrievedAtUtc: new Date().toISOString(), page: 1, pageSize: 50,
    totalCount: 60, members: [], coverageExplanation: 'Direct memberOf only; primary groups excluded.' } };
  mocks.invoke.mockReturnValue({ promise: Promise.resolve(value), cancel: mocks.cancel });
  page(value);
  fireEvent.click(await screen.findByRole('button', { name: 'Next member page' }));
  await waitFor(() => expect(screen.getByTestId('route').textContent).toBe('?page=2'));
  expect(mocks.invoke.mock.calls[1][2]).toEqual(expect.objectContaining({ read: 'DIRECTORY_MEMBERS', memberPage: 2 }));
  expect(await screen.findByText(/60 visible source matches/)).toBeTruthy();
  await waitFor(() => expect(mocks.invoke).toHaveBeenLastCalledWith('groups', 'getProfile', expect.objectContaining({ read: 'CACHED', memberPage: 2 })));
  fireEvent.click(screen.getByRole('button', { name: 'Back' }));
  await waitFor(() => expect(mocks.invoke).toHaveBeenLastCalledWith('groups', 'getProfile', expect.objectContaining({ read: 'CACHED', memberPage: 1 })));
});

it('ignores an old group response after navigation', async () => {
  let complete: (value: GroupProfileResult) => void = () => {};
  mocks.invoke.mockReturnValueOnce({ promise: new Promise<GroupProfileResult>(resolve => { complete = resolve; }), cancel: mocks.cancel });
  mocks.invoke.mockReturnValue({ promise: Promise.resolve({ ...profile(), title: 'Second group' }), cancel: mocks.cancel });
  page(); fireEvent.click(screen.getByRole('button', { name: 'Other group' }));
  await screen.findByRole('heading', { name: 'Second group' });
  await act(async () => complete(profile()));
  expect(screen.queryByRole('heading', { name: 'Cloud group' })).toBeNull();
});

it('resolves an exact DN only after the user starts its bounded source read', async () => {
  const dn = 'CN=Operations,DC=example,DC=test';
  mocks.invoke.mockReturnValueOnce({ promise: Promise.resolve({ kind: 'GROUP', source: 'ACTIVE_DIRECTORY', scope: 'example.test', id }), cancel: mocks.cancel });
  page(profile(), `/groups/resolve?scope=example.test&dn=${encodeURIComponent(dn)}`);
  expect(mocks.invoke).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole('button', { name: 'Resolve and open group' }));
  await waitFor(() => expect(mocks.invoke).toHaveBeenCalledTimes(2));
  expect(mocks.invoke.mock.calls[0]).toEqual(['groups', 'resolve', { directoryScope: 'example.test', distinguishedName: dn, securityIdentifier: null, connection: null }]);
  expect(mocks.invoke.mock.calls[1][2].reference).toEqual({ kind: 'GROUP', source: 'ACTIVE_DIRECTORY', scope: 'example.test', id });
});
