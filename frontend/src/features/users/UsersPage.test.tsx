import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ListUsersRequest, UserPageResult, UserSummary } from '../../shared/api-types';
import { TargetProvider } from '../../shared/targets/TargetContext';
import { UsersPage } from './UsersPage';
import { toUserDirectoryConnection } from './users';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));
vi.mock('../../shared/bridge/bridgeClient', () => ({
  invoke: invokeMock,
  BridgeInvokeError: class extends Error {},
  BridgeCancelledError: class extends Error {},
  BridgeTimeoutError: class extends Error {},
  BridgeUnavailableError: class extends Error {},
}));

const alex: UserSummary = {
  objectId: '00112233-4455-6677-8899-aabbccddeeff',
  displayName: 'Alex Example',
  samAccountName: 'a.example',
  userPrincipalName: 'a.example@corp.example',
  employeeId: 'E-1042',
  department: 'IT',
  title: 'Administrator',
  organizationalUnitPath: 'OU=Users,DC=corp,DC=example',
  enabled: true,
  replicatedLastLogonAtUtc: '2026-08-25T08:00:00Z',
};

function page(payload: ListUsersRequest, users: UserSummary[] = [alex]): UserPageResult {
  return {
    domainJoined: true,
    domainName: 'corp.example',
    baseDistinguishedName: payload.baseDistinguishedName ?? 'DC=corp,DC=example',
    page: payload.page ?? 1,
    pageSize: payload.pageSize ?? 25,
    totalCount: 51,
    users,
  };
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/users']}>
      <TargetProvider>
        <Routes>
          <Route path="/users" element={<UsersPage />} />
          <Route path="/users/:objectId" element={<div>User profile route</div>} />
        </Routes>
      </TargetProvider>
    </MemoryRouter>,
  );
}

describe('UsersPage', () => {
  beforeEach(() => {
    localStorage.clear();
    invokeMock.mockReset();
    invokeMock.mockImplementation((module: string, action: string, payload: ListUsersRequest) => {
      if (module === 'targets' && action === 'list') return Promise.resolve({
        targets: [{ id: 1, label: 'DC01', host: 'dc01.corp.example', role: 'DomainController', userName: null, createdAtUtc: '2026-08-26T12:00:00Z' }],
      });
      if (module === 'usermanagement' && action === 'listUsers') return Promise.resolve(page(payload));
      return Promise.resolve({});
    });
  });

  it('loads a stable server page through the preferred saved domain controller', async () => {
    renderPage();

    expect(await screen.findByText('Alex Example')).toBeDefined();
    expect(screen.getByText('51 matching accounts')).toBeDefined();
    expect(screen.getByText('1–1 of 51 users')).toBeDefined();
    expect(invokeMock).toHaveBeenCalledWith('usermanagement', 'listUsers', expect.objectContaining({
      page: 1,
      pageSize: 25,
      sortField: 'DISPLAY_NAME',
      sortDirection: 'ASCENDING',
      connection: { server: 'dc01.corp.example' },
    }));
  });

  it('applies bounded directory filters and server sorting without querying on each keystroke', async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText('Alex Example');
    const initialCalls = invokeMock.mock.calls.filter((call) => call[0] === 'usermanagement').length;

    await user.type(screen.getByRole('searchbox', { name: 'Search users' }), 'Ada');
    await user.type(screen.getByRole('textbox', { name: 'Department' }), 'Engineering');
    expect(invokeMock.mock.calls.filter((call) => call[0] === 'usermanagement')).toHaveLength(initialCalls);
    await user.click(screen.getByRole('button', { name: 'Apply filters' }));

    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith(
      'usermanagement',
      'listUsers',
      expect.objectContaining({ search: 'Ada', department: 'Engineering', page: 1 }),
    ));
    await user.click(screen.getByRole('button', { name: /Account/ }));
    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith(
      'usermanagement',
      'listUsers',
      expect.objectContaining({ sortField: 'SAM_ACCOUNT_NAME', sortDirection: 'ASCENDING' }),
    ));
  });

  it('opens User 360 from a keyboard-activatable result row', async () => {
    const user = userEvent.setup();
    renderPage();
    const row = (await screen.findByText('Alex Example')).closest('tr') as HTMLTableRowElement;
    row.focus();
    await user.keyboard('{Enter}');
    expect(await screen.findByText('User profile route')).toBeDefined();
  });

  it('requests subsequent pages from the server', async () => {
    const user = userEvent.setup();
    renderPage();
    const table = await screen.findByRole('table');
    await user.click(within(table.parentElement!.parentElement!).getByRole('button', { name: 'Next' }));
    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith(
      'usermanagement',
      'listUsers',
      expect.objectContaining({ page: 2 }),
    ));
  });

  it('normalizes directory credentials without adding empty fields', () => {
    expect(toUserDirectoryConnection(
      { domain: ' corp.example ', server: ' dc01 ' },
      { userName: ' reader ', domain: ' CORP ', password: 'session-secret' },
    )).toEqual({
      domain: 'corp.example',
      server: 'dc01',
      userName: 'reader',
      userDomain: 'CORP',
      password: 'session-secret',
    });
    expect(toUserDirectoryConnection({ domain: '', server: '' }, null)).toBeNull();
  });

  it('explains when no local directory is available', async () => {
    invokeMock.mockImplementation((module: string, action: string, payload: ListUsersRequest) => {
      if (module === 'targets' && action === 'list') return Promise.resolve({ targets: [] });
      if (module === 'usermanagement' && action === 'listUsers') return Promise.resolve({
        ...page(payload, []),
        domainJoined: false,
        domainName: null,
        baseDistinguishedName: null,
        totalCount: 0,
      });
      return Promise.resolve({});
    });

    renderPage();

    expect(await screen.findByText('No Active Directory domain')).toBeDefined();
    expect(screen.getByText(/name a domain or domain controller/)).toBeDefined();
  });

  it('keeps directory failures actionable and retryable', async () => {
    invokeMock.mockImplementation((module: string, action: string) => {
      if (module === 'targets' && action === 'list') return Promise.resolve({ targets: [] });
      if (module === 'usermanagement' && action === 'listUsers') {
        return Promise.reject(new Error('LDAP bind failed with internal detail'));
      }
      return Promise.resolve({});
    });

    renderPage();

    const alert = await screen.findByRole('alert');
    expect(within(alert).getByText('The directory user inventory could not be loaded.')).toBeDefined();
    expect(within(alert).getByText('Next action')).toBeDefined();
    expect(within(alert).getByRole('button', { name: 'Retry' })).toBeDefined();
  });
});
