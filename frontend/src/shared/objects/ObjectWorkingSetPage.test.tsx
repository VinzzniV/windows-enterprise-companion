import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { captureWorkingSet, type WorkingSetRead } from './workingSet';
import { DevicesWorkingSetPage, UsersWorkingSetPage } from './ObjectWorkingSetPage';
import { ScopedUserProfilePage } from '../../features/users/ScopedUserProfilePage';
import { GroupProfilePage } from '../../features/groups/GroupProfilePage';

const { workspaceMock, invokeMock, userProfileMock, groupProfileMock } = vi.hoisted(() => ({
  workspaceMock: vi.fn(), invokeMock: vi.fn(), userProfileMock: vi.fn(), groupProfileMock: vi.fn(),
}));
vi.mock('./WorkingSetContext', () => ({ useWorkingSet: workspaceMock }));
vi.mock('../targets/TargetContext', () => ({ useTargets: () => ({ adminCredentials: null }) }));
vi.mock('../bridge/bridgeClient', async importOriginal => ({ ...await importOriginal<typeof import('../bridge/bridgeClient')>(), invokeCancellable: invokeMock }));
vi.mock('../../features/users/useUserProfile', () => ({ useUserProfile: userProfileMock }));
vi.mock('../../features/groups/useGroupProfile', () => ({ useGroupProfile: groupProfileMock }));
vi.mock('../viewCache', () => ({ loadView: () => null }));
const tenant = '11111111-1111-1111-1111-111111111111';
const groupId = '33333333-3333-3333-3333-333333333333';
const read: WorkingSetRead = { key: 'users', title: 'Entra users', family: 'cloud', scope: tenant, sessionRevision: 1, revision: 1,
  retrievedAtUtc: null, lastAttemptAtUtc: null, retainedUntilUtc: null, freshUntilUtc: null,
  availability: 'available', coverage: 'partial', error: null, sourceTotal: 5000, cachedRecordCount: 60, limited: false,
  rows: Array.from({ length: 60 }, (_, index) => ({ kind: 'USER', source: 'ENTRA', label: index === 0 ? 'Needle account' : `Account ${String(index).padStart(2, '0')}`, aliases: [],
    reference: { kind: 'USER', source: 'ENTRA', scope: tenant, id: `22222222-2222-2222-2222-${String(index + 1).padStart(12, '0')}` },
    accountEnabled: index % 2 === 0 ? true : null, operatingSystem: null, sid: null, registrationDeviceId: null, assignedSkuIds: null,
    userPrincipalName: index === 0 ? 'needle@example.test' : `account${index}@example.test` })) };
function Location() { const location = useLocation(); return <><output data-testid="url">{location.pathname + location.search}</output>
  <output data-testid="route-state">{JSON.stringify(location.state)}</output></>; }
function view(initial = '/users/workspace?page=2') { return render(<MemoryRouter initialEntries={[initial]}>
  <div data-testid="application-scroll-container"><Routes>
    <Route path="/users/workspace" element={<UsersWorkingSetPage />} />
    <Route path="/devices" element={<DevicesWorkingSetPage />} />
    <Route path="/users/:source/:scope/:objectId" element={<ScopedUserProfilePage />} />
    <Route path="/groups/:source/:scope/:objectId" element={<GroupProfilePage />} />
  </Routes><Location /></div>
</MemoryRouter>); }
beforeEach(() => {
  const policy = { maximumRecords: 100, maximumSourceReads: 128, directoryScope: null, tenantId: tenant };
  const snapshot = captureWorkingSet([read], policy, Date.now());
  workspaceMock.mockReturnValue({ displayed: snapshot, available: snapshot, policy, directoryEndpoint: null, busy: false, error: null,
    hasUpdates: false, cloudGeneration: 0, directoryGeneration: 0, refreshCached: vi.fn(), applyUpdates: vi.fn(), publishDirectory: vi.fn(), clearFamily: vi.fn() });
  invokeMock.mockReset();
  userProfileMock.mockReturnValue({
    data: {
      reference: read.rows[0].reference, title: 'Needle account', identity: 'SCOPED_ID', explanation: 'Tenant-scoped account.',
      directory: null, adProfile: null, entraUser: null, relationships: [{
        target: { kind: 'GROUP', source: 'ENTRA', scope: tenant, id: groupId }, label: 'Operations',
        relation: 'Direct group (Entra)', evidence: 'SCOPED_ID', explanation: 'Direct membership only.',
      }], candidates: [], sourceErrors: [], cloud: null,
    },
    busy: false, error: null, now: Date.now(), load: vi.fn(), cancel: vi.fn(),
  });
  groupProfileMock.mockReturnValue({
    data: {
      reference: { kind: 'GROUP', source: 'ENTRA', scope: tenant, id: groupId }, title: 'Operations',
      identity: 'SCOPED_ID', explanation: 'Tenant-scoped group.', directory: null, directoryMembers: null,
      cloud: null, relationships: [], sourceErrors: [],
    },
    busy: false, error: null, now: Date.now(), load: vi.fn(), cancel: vi.fn(),
  });
});

describe('object working-set list', () => {
  it('filters the whole displayed set before paging, resets the page, and never queries a source on typing', () => {
    view();
    const table = screen.getByRole('table');
    expect(within(table).queryByText('Needle account')).toBeNull();
    expect(within(table).getAllByText('Account 26').length).toBeGreaterThan(0);
    fireEvent.change(screen.getByLabelText('Search loaded users'), { target: { value: 'Needle' } });
    expect(screen.getByTestId('url').textContent).toBe('/users/workspace?q=Needle');
    expect(within(table).getAllByText('Needle account').length).toBeGreaterThan(0);
    expect(screen.getByText(/Showing 1 of 1 matching users from 60 loaded/)).toBeDefined();
    expect(within(table).getByRole('link', { name: 'Needle account' })).toBeTruthy();
    expect(within(table).getAllByText('needle@example.test').length).toBeGreaterThan(0);
    expect(screen.getByText(/Source exceptions: Entra users \(partial\)/)).toBeTruthy();
    expect(invokeMock).not.toHaveBeenCalled();
  });

  it('sorts beyond the current page and restores filters and page through a scoped profile journey', async () => {
    view('/users/workspace?account=unknown&page=2');
    const table = screen.getByRole('table');
    const link = within(table).getAllByRole('link')[0];
    const original = link.getAttribute('href');
    const scrollContainer = screen.getByTestId('application-scroll-container');
    scrollContainer.scrollTop = 180;
    (link as HTMLElement).focus();
    fireEvent.click(link);
    fireEvent.click(await screen.findByRole('link', { name: /Back to results/ }));
    await waitFor(() => expect(screen.getByTestId('url').textContent).toBe('/users/workspace?account=unknown&page=2'));
    expect(within(screen.getByRole('table')).getAllByRole('link')[0].getAttribute('href')).toBe(original);
    expect(scrollContainer.scrollTop).toBe(180);
    expect(document.activeElement?.getAttribute('data-object-focus')).toBe(original);
    fireEvent.click(screen.getByRole('button', { name: 'Account' }));
    fireEvent.click(screen.getByRole('button', { name: 'Account' }));
    expect(screen.getByTestId('url').textContent).toContain('sort=asc');
    expect(screen.getByTestId('url').textContent).not.toContain('page=2');
    expect(within(screen.getByRole('table')).getAllByText('Account 01').length).toBeGreaterThan(0);
  });

  it('opens a carried global query as an explicit source-search preparation and exposes first-level device modes', () => {
    view('/users/workspace?q=outside&source=ACTIVE_DIRECTORY&discover=ad');
    expect((screen.getByLabelText('Source query') as HTMLInputElement).value).toBe('outside');
    expect((screen.getByText('Load or search a source').closest('details') as HTMLDetailsElement).open).toBe(true);
    expect(invokeMock).not.toHaveBeenCalled();

    view('/devices');
    const modes = screen.getByRole('navigation', { name: 'Device work modes' });
    expect(within(modes).getByRole('link', { name: 'Source records' }).getAttribute('aria-current')).toBe('page');
    expect(within(modes).getByRole('link', { name: 'Operational view' }).getAttribute('href')).toBe('/clients');
    expect(within(modes).getByRole('link', { name: 'Compare' })).toBeTruthy();
    expect(within(modes).getByRole('link', { name: 'Device Cleanup' })).toBeTruthy();
    expect(within(modes).getByRole('link', { name: 'AD analysis' })).toBeTruthy();
  });

  it('retains the originating result through a real profile relationship hop', async () => {
    view(`/users/workspace?q=Account&source=ENTRA&tenant=${tenant}&sort=desc&page=2&size=25`);
    fireEvent.click(within(screen.getByRole('table')).getAllByRole('link')[0]);
    fireEvent.click(await screen.findByRole('tab', { name: 'Groups' }));
    fireEvent.click(screen.getByRole('link', { name: 'Operations' }));
    expect(screen.getByTestId('route-state').textContent).toContain('fromWorkingSet');
    fireEvent.click(await screen.findByRole('link', { name: /Back to results/ }));
    await waitFor(() => expect(screen.getByTestId('url').textContent)
      .toBe(`/users/workspace?q=Account&source=ENTRA&tenant=${tenant}&sort=desc&page=2&size=25`));
    expect(invokeMock).not.toHaveBeenCalled();
  });
});
