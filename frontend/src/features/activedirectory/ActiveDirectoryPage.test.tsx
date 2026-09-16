import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { AdHygieneResult, AdOverviewResult } from '../../shared/api-types';
import { BridgeInvokeError } from '../../shared/bridge/bridgeClient';
import { TargetProvider } from '../../shared/targets/TargetContext';
import { ActiveDirectoryPage } from './ActiveDirectoryPage';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../shared/bridge/bridgeClient', async (importOriginal) => {
  const original = await importOriginal<typeof import('../../shared/bridge/bridgeClient')>();
  return { ...original, invoke: invokeMock };
});

const workgroupOverview: AdOverviewResult = {
  domainJoined: false,
  domainName: null,
  defaultNamingContext: null,
  domainControllers: [],
  userCount: 0,
  disabledUserCount: 0,
  groupCount: 0,
  computerCount: 0,
  capturedAtUtc: '2026-08-19T07:00:00Z',
};

const domainOverview: AdOverviewResult = {
  domainJoined: true,
  domainName: 'corp.example.com',
  defaultNamingContext: 'DC=corp,DC=example,DC=com',
  domainControllers: [
    {
      hostName: 'dc01.corp.example.com',
      distinguishedName: 'CN=DC01,OU=Domain Controllers,DC=corp,DC=example,DC=com',
    },
  ],
  userCount: 25_000,
  disabledUserCount: 125,
  groupCount: 1_200,
  computerCount: 4_500,
  capturedAtUtc: '2026-08-19T07:00:00Z',
};

const hygiene: AdHygieneResult = {
  domainJoined: true,
  domainName: 'corp.example.com',
  privilegedGroups: [
    {
      groupName: 'Domain Admins',
      distinguishedName: 'CN=Domain Admins,CN=Users,DC=corp,DC=example,DC=com',
      directMemberCount: 2,
      memberDistinguishedNames: [
        'CN=Doe\\, Jane,OU=Privileged,OU=Users,DC=corp,DC=example,DC=com',
      ],
    },
  ],
  rules: [
    {
      ruleId: 'WEC-AD-EMPTY-GROUPS',
      title: 'Empty groups',
      matchCount: 0,
      examples: [],
      recommendation: 'No action required.',
    },
    {
      ruleId: 'WEC-AD-INACTIVE-USERS',
      title: 'Inactive users',
      matchCount: 250,
      examples: [
        {
          name: 'example.user',
          distinguishedName: 'CN=Example User,OU=Stale Accounts,DC=corp,DC=example,DC=com',
          lastLogonUtc: null,
        },
      ],
      recommendation: 'Review stale accounts.',
    },
  ],
  capturedAtUtc: '2026-08-19T07:01:00Z',
};

function renderPage(): void {
  render(
    <TargetProvider>
      <ActiveDirectoryPage />
    </TargetProvider>,
  );
}

describe('ActiveDirectoryPage', () => {
  beforeEach(() => {
    localStorage.clear();
    invokeMock.mockReset();
    invokeMock.mockImplementation((_module: string, action: string) => {
      if (action === 'list') return Promise.resolve({ targets: [] });
      return Promise.reject(new Error(`Unexpected action: ${action}`));
    });
  });

  afterEach(() => {
    Reflect.deleteProperty(HTMLElement.prototype, 'scrollIntoView');
  });

  it('turns a known workgroup result into one preflight state and disables hygiene', async () => {
    invokeMock.mockImplementation((_module: string, action: string) => {
      if (action === 'list') return Promise.resolve({ targets: [] });
      if (action === 'getOverview') return Promise.resolve(workgroupOverview);
      return Promise.reject(new Error(`Unexpected action: ${action}`));
    });
    renderPage();

    await userEvent.click(screen.getByRole('button', { name: 'Analyze directory' }));

    expect(await screen.findByText('No local Active Directory domain')).toBeDefined();
    expect(screen.getByRole('button', { name: 'Hygiene unavailable (workgroup)' }).hasAttribute('disabled')).toBe(true);
    expect(screen.queryByText('Hygiene checks')).toBeNull();
  });

  it('re-enables directory actions when an explicit foreign domain is entered', async () => {
    invokeMock.mockImplementation((_module: string, action: string) => {
      if (action === 'list') return Promise.resolve({ targets: [] });
      if (action === 'testConnection') {
        return Promise.resolve({ domainJoined: false, domainName: null, defaultNamingContext: null });
      }
      return Promise.reject(new Error(`Unexpected action: ${action}`));
    });
    renderPage();

    await userEvent.click(screen.getByRole('button', { name: 'Test connection' }));
    expect(await screen.findByRole('button', { name: 'Hygiene unavailable (workgroup)' })).toBeDefined();

    await userEvent.type(screen.getByRole('textbox', { name: 'Domain' }), 'corp.example.com');

    expect(screen.queryByText('No local Active Directory domain')).toBeNull();
    expect(screen.getByRole('button', { name: 'Run hygiene checks' }).hasAttribute('disabled')).toBe(false);
  });

  it('renders exact large-directory counts and bounded hygiene examples', async () => {
    invokeMock.mockImplementation((_module: string, action: string) => {
      if (action === 'list') return Promise.resolve({ targets: [] });
      if (action === 'getOverview') return Promise.resolve(domainOverview);
      if (action === 'getHygiene') return Promise.resolve(hygiene);
      return Promise.reject(new Error(`Unexpected action: ${action}`));
    });
    renderPage();

    await userEvent.click(screen.getByRole('button', { name: 'Analyze directory' }));
    expect(await screen.findByText(/25[.,]000/)).toBeDefined();
    expect(screen.getByText(/4[.,]500/)).toBeDefined();

    await userEvent.click(screen.getByRole('button', { name: 'Run hygiene checks' }));
    expect(await screen.findByText('Inactive users')).toBeDefined();
    expect(screen.getByText('250')).toBeDefined();
    expect(screen.getByText('Showing 1 loaded example of 250 matches.')).toBeDefined();
  });

  it('exports the complete analysis, including every paged hygiene match and privileged member', async () => {
    invokeMock.mockImplementation((_module: string, action: string, payload?: Record<string, unknown>) => {
      if (action === 'list') return Promise.resolve({ targets: [] });
      if (action === 'getOverview') return Promise.resolve(domainOverview);
      if (action === 'getHygiene') return Promise.resolve(hygiene);
      if (action === 'getPrivilegedGroupMemberPage') {
        return Promise.resolve({
          groupName: 'Domain Admins',
          groupDistinguishedName: hygiene.privilegedGroups[0].distinguishedName,
          page: 1,
          pageSize: 100,
          totalCount: 2,
          items: [
            {
              accountName: 'jane.doe',
              distinguishedName: hygiene.privilegedGroups[0].memberDistinguishedNames[0],
              entityType: 'User',
              accountStatus: 'Enabled',
              lastLogonUtc: null,
            },
            {
              accountName: 'john.doe',
              distinguishedName: 'CN=John Doe,OU=Privileged,OU=Users,DC=corp,DC=example,DC=com',
              entityType: 'User',
              accountStatus: 'Enabled',
              lastLogonUtc: null,
            },
          ],
        });
      }
      if (action === 'getHygieneRulePage') {
        const page = Number(payload?.page);
        const itemCount = page === 3 ? 50 : 100;
        return Promise.resolve({
          ruleId: 'WEC-AD-INACTIVE-USERS',
          page,
          pageSize: 100,
          totalCount: 250,
          items: Array.from({ length: itemCount }, (_, index) => ({
            name: `stale.user.${(page - 1) * 100 + index + 1}`,
            distinguishedName: `CN=Stale User ${(page - 1) * 100 + index + 1},OU=Stale Accounts,DC=corp,DC=example,DC=com`,
            lastLogonUtc: null,
          })),
          evaluatedAtUtc: hygiene.capturedAtUtc,
        });
      }
      if (action === 'exportCsv') return Promise.resolve({ cancelled: false, filePath: 'C:\\temp\\ad.csv' });
      return Promise.reject(new Error(`Unexpected action: ${action}`));
    });
    renderPage();

    const exportButton = screen.getByRole('button', { name: 'Export complete CSV' });
    expect(exportButton).toHaveProperty('disabled', true);
    await userEvent.click(screen.getByRole('button', { name: 'Analyze directory' }));
    await userEvent.click(screen.getByRole('button', { name: 'Run hygiene checks' }));
    await userEvent.click(await screen.findByRole('button', { name: 'Export complete CSV' }));

    expect(await screen.findByText('Complete export saved to C:\\temp\\ad.csv')).toBeDefined();
    expect(invokeMock.mock.calls.filter((call) => call[1] === 'getPrivilegedGroupMemberPage')).toHaveLength(1);
    expect(invokeMock.mock.calls.filter((call) => call[1] === 'getHygieneRulePage')).toHaveLength(3);
    const exportCall = invokeMock.mock.calls.find((call) => call[1] === 'exportCsv');
    expect(exportCall).toBeDefined();
    expect(exportCall?.[2].csv).toContain('CN=Stale User 250,OU=Stale Accounts');
    expect(exportCall?.[2].csv).toContain('CN=John Doe,OU=Privileged');
  });

  it('turns bounded hygiene examples into searchable identity tables with honest coverage', async () => {
    invokeMock.mockImplementation((_module: string, action: string) => {
      if (action === 'list') return Promise.resolve({ targets: [] });
      if (action === 'getHygiene') return Promise.resolve(hygiene);
      return Promise.reject(new Error(`Unexpected action: ${action}`));
    });
    renderPage();

    await userEvent.click(screen.getByRole('button', { name: 'Run hygiene checks' }));

    expect(await screen.findByText('Doe, Jane')).toBeDefined();
    expect(screen.getByText('corp.example.com / Users / Privileged')).toBeDefined();
    expect(screen.getByText('Showing 1 loaded example of 2 direct members.')).toBeDefined();
    expect(screen.getByText('Showing 1 loaded example of 250 matches.')).toBeDefined();
    expect(screen.queryByText('CN=Doe\\, Jane,OU=Privileged,OU=Users,DC=corp,DC=example,DC=com')).toBeNull();

    const search = screen.getByRole('searchbox', { name: 'Search loaded hygiene identities' });
    await userEvent.type(search, 'Jane');
    expect(screen.getByText('Doe, Jane')).toBeDefined();
    expect(screen.queryByText('Example User')).toBeNull();
    expect(screen.getAllByText('No loaded identities match this search.')).toHaveLength(2);
  });

  it('opens keyboard-accessible identity details and reports clipboard outcomes', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    const scrollIntoView = vi.fn();
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } });
    Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', { configurable: true, value: scrollIntoView });
    invokeMock.mockImplementation((_module: string, action: string) => {
      if (action === 'list') return Promise.resolve({ targets: [] });
      if (action === 'getHygiene') return Promise.resolve(hygiene);
      return Promise.reject(new Error(`Unexpected action: ${action}`));
    });
    renderPage();
    await userEvent.click(screen.getByRole('button', { name: 'Run hygiene checks' }));

    const row = (await screen.findByText('Example User')).closest('tr');
    expect(row).not.toBeNull();
    row!.focus();
    await userEvent.keyboard('{Enter}');

    const details = screen.getByRole('region', { name: 'Directory identity details for Example User' });
    expect(details.textContent).toContain('corp.example.com / Stale Accounts');
    expect(details.textContent).toContain('Review stale accounts.');
    await waitFor(() => expect(scrollIntoView).toHaveBeenCalledWith({ block: 'nearest' }));
    await userEvent.click(screen.getByRole('button', { name: 'Copy DN for Example User' }));
    expect(writeText).toHaveBeenCalledWith('CN=Example User,OU=Stale Accounts,DC=corp,DC=example,DC=com');
    expect(await screen.findByText('DN copied.')).toBeDefined();

    writeText.mockRejectedValueOnce(new Error('clipboard denied'));
    await userEvent.click(screen.getByRole('button', { name: 'Copy DN for Example User' }));
    expect(await screen.findByText('Copy failed.')).toBeDefined();
  });

  it('loads full rule matches only on demand and pages them with the original evaluation time', async () => {
    const pageItems = Array.from({ length: 50 }, (_, index) => ({
      name: `stale.user.${index + 1}`,
      distinguishedName: `CN=Stale User ${index + 1},OU=Stale Accounts,DC=corp,DC=example,DC=com`,
      lastLogonUtc: null,
    }));
    invokeMock.mockImplementation((_module: string, action: string, payload?: Record<string, unknown>) => {
      if (action === 'list') return Promise.resolve({ targets: [] });
      if (action === 'getHygiene') return Promise.resolve(hygiene);
      if (action === 'getHygieneRulePage') {
        const page = Number(payload?.page ?? 1);
        return Promise.resolve({
          ruleId: 'WEC-AD-INACTIVE-USERS',
          page,
          pageSize: 50,
          totalCount: 250,
          items: pageItems.map((item, index) => ({ ...item, name: `stale.user.${(page - 1) * 50 + index + 1}` })),
          evaluatedAtUtc: hygiene.capturedAtUtc,
        });
      }
      return Promise.reject(new Error(`Unexpected action: ${action}`));
    });
    renderPage();
    await userEvent.click(screen.getByRole('button', { name: 'Run hygiene checks' }));

    expect(await screen.findByRole('button', { name: 'Browse all 250 matches' })).toBeDefined();
    expect(invokeMock.mock.calls.filter((call) => call[1] === 'getHygieneRulePage')).toHaveLength(0);

    await userEvent.click(screen.getByRole('button', { name: 'Browse all 250 matches' }));
    expect(await screen.findByText('1–50 of 250 matches')).toBeDefined();
    expect(invokeMock).toHaveBeenCalledWith('activedirectory', 'getHygieneRulePage', {
      connection: null,
      ruleId: 'WEC-AD-INACTIVE-USERS',
      evaluatedAtUtc: hygiene.capturedAtUtc,
      query: null,
      page: 1,
      pageSize: 50,
    });

    fireEvent.click(screen.getByText('Next', { selector: 'button' }));
    expect(await screen.findByText('51–100 of 250 matches')).toBeDefined();
    expect(invokeMock).toHaveBeenCalledWith('activedirectory', 'getHygieneRulePage', expect.objectContaining({
      page: 2,
      evaluatedAtUtc: hygiene.capturedAtUtc,
    }));

    fireEvent.change(screen.getByLabelText('Search all matches for Inactive users'), { target: { value: 'ops*(admin)' } });
    expect(invokeMock.mock.calls.filter(call => call[1] === 'getHygieneRulePage')).toHaveLength(2);
    fireEvent.click(screen.getByText('Search directory', { selector: 'button' }));
    expect(invokeMock).toHaveBeenCalledWith('activedirectory', 'getHygieneRulePage', expect.objectContaining({
      page: 1,
      query: 'ops*(admin)',
    }));
  });

  it('loads typed privileged group members only on demand and pages a server-side search', async () => {
    const groupHygiene: AdHygieneResult = {
      ...hygiene,
      privilegedGroups: [{ ...hygiene.privilegedGroups[0], directMemberCount: 75 }],
    };
    const member = (index: number) => ({
      accountName: `admin.${index}`,
      distinguishedName: `CN=Admin ${index},OU=Privileged,DC=corp,DC=example,DC=com`,
      entityType: 'User',
      accountStatus: index === 1 ? 'Disabled' : 'Enabled',
      lastLogonUtc: null,
    });
    invokeMock.mockImplementation((_module: string, action: string, payload?: Record<string, unknown>) => {
      if (action === 'list') return Promise.resolve({ targets: [] });
      if (action === 'getHygiene') return Promise.resolve(groupHygiene);
      if (action === 'getPrivilegedGroupMemberPage') {
        const page = Number(payload?.page ?? 1);
        const query = payload?.query;
        const count = query ? 1 : page === 1 ? 50 : 25;
        return Promise.resolve({
          groupName: 'Domain Admins',
          groupDistinguishedName: groupHygiene.privilegedGroups[0].distinguishedName,
          page,
          pageSize: 50,
          totalCount: query ? 1 : 75,
          items: Array.from({ length: count }, (_, index) => member((page - 1) * 50 + index + 1)),
        });
      }
      return Promise.reject(new Error(`Unexpected action: ${action}`));
    });
    renderPage();
    await userEvent.click(screen.getByRole('button', { name: 'Run hygiene checks' }));

    expect(await screen.findByRole('button', { name: 'Browse all 75 direct members of Domain Admins' })).toBeDefined();
    expect(invokeMock.mock.calls.filter((call) => call[1] === 'getPrivilegedGroupMemberPage')).toHaveLength(0);

    await userEvent.click(screen.getByRole('button', { name: 'Browse all 75 direct members of Domain Admins' }));
    expect(await screen.findByText('1–50 of 75 direct members')).toBeDefined();
    expect((await screen.findAllByText('Disabled')).length).toBeGreaterThan(0);
    expect((await screen.findAllByText('Current')).length).toBeGreaterThan(0);
    expect((await screen.findAllByText('Account enabled')).length).toBeGreaterThan(0);
    expect(screen.queryByText('Enabled')).toBeNull();
    expect(invokeMock).toHaveBeenCalledWith('activedirectory', 'getPrivilegedGroupMemberPage', {
      connection: null,
      groupDistinguishedName: groupHygiene.privilegedGroups[0].distinguishedName,
      query: null,
      page: 1,
      pageSize: 50,
    });

    fireEvent.click(screen.getByText('Next', { selector: 'button' }));
    expect(await screen.findByText('51–75 of 75 direct members')).toBeDefined();

    fireEvent.change(screen.getByLabelText('Search all direct members of Domain Admins'), { target: { value: 'ops*(admin)' } });
    expect(invokeMock.mock.calls.filter(call => call[1] === 'getPrivilegedGroupMemberPage')).toHaveLength(2);
    fireEvent.click(screen.getByText('Search group', { selector: 'button' }));
    expect(invokeMock).toHaveBeenCalledWith('activedirectory', 'getPrivilegedGroupMemberPage', expect.objectContaining({
      page: 1,
      query: 'ops*(admin)',
    }));
  });

  it('keeps privileged previews available when a full group page fails and retries locally', async () => {
    let attempts = 0;
    invokeMock.mockImplementation((_module: string, action: string) => {
      if (action === 'list') return Promise.resolve({ targets: [] });
      if (action === 'getHygiene') return Promise.resolve(hygiene);
      if (action === 'getPrivilegedGroupMemberPage') {
        attempts += 1;
        if (attempts === 1) return Promise.reject(new Error('LDAP member page failed'));
        return Promise.resolve({
          groupName: 'Domain Admins',
          groupDistinguishedName: hygiene.privilegedGroups[0].distinguishedName,
          page: 1,
          pageSize: 50,
          totalCount: 1,
          items: [{
            accountName: 'jane.doe',
            distinguishedName: hygiene.privilegedGroups[0].memberDistinguishedNames[0],
            entityType: 'User',
            accountStatus: 'Enabled',
            lastLogonUtc: null,
          }],
        });
      }
      return Promise.reject(new Error(`Unexpected action: ${action}`));
    });
    renderPage();
    await userEvent.click(screen.getByRole('button', { name: 'Run hygiene checks' }));
    await userEvent.click(await screen.findByRole('button', { name: 'Browse all 2 direct members of Domain Admins' }));

    expect(await screen.findByText('The complete privileged group membership could not be loaded.')).toBeDefined();
    expect(screen.getByText('Doe, Jane')).toBeDefined();
    await userEvent.click(screen.getByRole('button', { name: 'Retry full privileged group membership' }));
    expect(await screen.findByText('1–1 of 1 direct members')).toBeDefined();
    expect(attempts).toBe(2);
  });

  it('keeps the bounded rule preview available when the full list fails and retries locally', async () => {
    let pageAttempts = 0;
    invokeMock.mockImplementation((_module: string, action: string) => {
      if (action === 'list') return Promise.resolve({ targets: [] });
      if (action === 'getHygiene') return Promise.resolve(hygiene);
      if (action === 'getHygieneRulePage') {
        pageAttempts += 1;
        if (pageAttempts === 1) return Promise.reject(new Error('LDAP page failed'));
        return Promise.resolve({
          ruleId: 'WEC-AD-INACTIVE-USERS',
          page: 1,
          pageSize: 50,
          totalCount: 1,
          items: hygiene.rules[0].examples,
          evaluatedAtUtc: hygiene.capturedAtUtc,
        });
      }
      return Promise.reject(new Error(`Unexpected action: ${action}`));
    });
    renderPage();
    await userEvent.click(screen.getByRole('button', { name: 'Run hygiene checks' }));
    await userEvent.click(await screen.findByRole('button', { name: 'Browse all 250 matches' }));

    expect(await screen.findByText('The complete hygiene matches could not be loaded.')).toBeDefined();
    expect(screen.getByText('Example User')).toBeDefined();
    await userEvent.click(screen.getByRole('button', { name: 'Retry full hygiene list' }));
    expect(await screen.findByText('1–1 of 1 matches')).toBeDefined();
    expect(pageAttempts).toBe(2);
  });

  it('presents a failed connection test with directory guidance and closed technical evidence', async () => {
    invokeMock.mockImplementation((_module: string, action: string) => {
      if (action === 'list') return Promise.resolve({ targets: [] });
      if (action === 'testConnection') {
        return Promise.reject(
          new BridgeInvokeError({
            code: 'DNS_RESOLUTION_FAILED',
            message: 'Domain lookup failed',
            details: 'No such host is known',
          }),
        );
      }
      return Promise.reject(new Error(`Unexpected action: ${action}`));
    });
    renderPage();

    await userEvent.click(screen.getByRole('button', { name: 'Test connection' }));

    expect(await screen.findByText('The directory connection could not be verified.')).toBeDefined();
    expect(screen.getByText('DNS did not return an address for the supplied host name.')).toBeDefined();
    expect(
      screen.getByText(
        "Point this machine's DNS at a server that knows the domain (usually a domain controller), or enter a specific DC above.",
      ),
    ).toBeDefined();
    const details = screen.getByText('Technical details').closest('details');
    expect(details?.hasAttribute('open')).toBe(false);
    expect(screen.getByText(/Code: DNS_RESOLUTION_FAILED/)).toBeDefined();
    expect(screen.getByRole('button', { name: 'Test connection' })).toBeDefined();
  });

  it('presents an overview failure with a local retry', async () => {
    invokeMock.mockImplementation((_module: string, action: string) => {
      if (action === 'list') return Promise.resolve({ targets: [] });
      if (action === 'getOverview') return Promise.reject(new Error('LDAP parser exploded'));
      return Promise.reject(new Error(`Unexpected action: ${action}`));
    });
    renderPage();

    await userEvent.click(screen.getByRole('button', { name: 'Analyze directory' }));

    expect(await screen.findByText('The directory overview could not be loaded.')).toBeDefined();
    const retry = screen.getByRole('button', { name: 'Retry directory analysis' });
    await userEvent.click(retry);
    await waitFor(() => {
      expect(invokeMock.mock.calls.filter((call) => call[1] === 'getOverview')).toHaveLength(2);
    });
  });

  it('presents a hygiene failure with a local retry', async () => {
    invokeMock.mockImplementation((_module: string, action: string) => {
      if (action === 'list') return Promise.resolve({ targets: [] });
      if (action === 'getHygiene') return Promise.reject(new Error('Rule query failed'));
      return Promise.reject(new Error(`Unexpected action: ${action}`));
    });
    renderPage();

    await userEvent.click(screen.getByRole('button', { name: 'Run hygiene checks' }));

    expect(await screen.findByText('The directory hygiene checks could not be completed.')).toBeDefined();
    const retry = screen.getByRole('button', { name: 'Retry hygiene checks' });
    await userEvent.click(retry);
    await waitFor(() => {
      expect(invokeMock.mock.calls.filter((call) => call[1] === 'getHygiene')).toHaveLength(2);
    });
  });
});
