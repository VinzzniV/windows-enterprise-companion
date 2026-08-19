import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { AdHygieneResult, AdOverviewResult } from '../../shared/api-types';
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
  privilegedGroups: [],
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
          distinguishedName: 'CN=Example User,DC=corp,DC=example,DC=com',
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
    expect(screen.getByText('Showing 1 of 250 matches.')).toBeDefined();

    const ruleTitles = [...document.querySelectorAll('summary')].map((summary) => summary.textContent ?? '');
    expect(ruleTitles.findIndex((title) => title.includes('Inactive users'))).toBeLessThan(
      ruleTitles.findIndex((title) => title.includes('Empty groups')),
    );
  });
});
