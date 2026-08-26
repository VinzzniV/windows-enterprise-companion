import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { UserProfileResult } from '../../shared/api-types';
import { TargetProvider } from '../../shared/targets/TargetContext';
import { UserDetailPage } from './UserDetailPage';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));
vi.mock('../../shared/bridge/bridgeClient', () => ({
  invoke: invokeMock,
  BridgeInvokeError: class extends Error {},
  BridgeCancelledError: class extends Error {},
  BridgeTimeoutError: class extends Error {},
  BridgeUnavailableError: class extends Error {},
}));

const profile: UserProfileResult = {
  identity: {
    objectId: '00112233-4455-6677-8899-aabbccddeeff',
    sid: 'S-1-5-21-100-200-300-1104',
    displayName: 'Alex Example',
    samAccountName: 'a.example',
    userPrincipalName: 'a.example@corp.example',
    mail: 'alex@example.test',
    employeeId: 'E-1042',
    department: 'IT',
    title: 'Administrator',
    managerDistinguishedName: 'CN=Manager,OU=Users,DC=corp,DC=example',
    distinguishedName: 'CN=Alex Example,OU=Users,DC=corp,DC=example',
    organizationalUnitPath: 'OU=Users,DC=corp,DC=example',
  },
  lifecycle: {
    enabled: true,
    createdAtUtc: '2024-01-02T12:00:00Z',
    accountExpiresAtUtc: null,
    replicatedLastLogonAtUtc: '2026-08-25T08:00:00Z',
    passwordLastSetAtUtc: '2026-07-01T08:00:00Z',
    passwordExpiresAtUtc: '2026-09-01T08:00:00Z',
    passwordNeverExpires: false,
  },
  access: {
    directGroups: [
      { name: 'Domänen-Admins', distinguishedName: 'CN=Domänen-Admins,CN=Users,DC=corp,DC=example' },
      { name: 'GG-App', distinguishedName: 'CN=GG-App,OU=Groups,DC=corp,DC=example' },
    ],
    privilegedCoverage: 'AVAILABLE',
    privilegedCoverageExplanation: 'Direct memberships were compared with the SID-validated privileged-group allowlist.',
    directPrivilegedGroups: [
      { name: 'Domänen-Admins', distinguishedName: 'CN=Domänen-Admins,CN=Users,DC=corp,DC=example' },
    ],
  },
  devices: {
    coverage: 'AVAILABLE',
    explanation: 'No stored Inventory devices are available for relationship evaluation.',
    sourceCoverage: {
      storedDeviceCount: 0,
      evidenceCapturedDeviceCount: 0,
      notCapturedDeviceCount: 0,
      unavailableDeviceCount: 0,
      truncatedDeviceCount: 0,
    },
    totalLinkedDeviceCount: 0,
    linkedDevicesTruncated: false,
    linkedDevices: [],
  },
};

function renderProfile(value: UserProfileResult = profile) {
  invokeMock.mockImplementation((module: string, action: string) => {
    if (module === 'targets' && action === 'list') return Promise.resolve({ targets: [] });
    if (module === 'usermanagement' && action === 'getUserProfile') return Promise.resolve(value);
    return Promise.resolve({});
  });
  return render(
    <MemoryRouter initialEntries={['/users/00112233-4455-6677-8899-aabbccddeeff']}>
      <TargetProvider>
        <Routes>
          <Route path="/users/:objectId" element={<UserDetailPage />} />
        </Routes>
      </TargetProvider>
    </MemoryRouter>,
  );
}

describe('UserDetailPage', () => {
  beforeEach(() => {
    localStorage.clear();
    invokeMock.mockReset();
  });

  it('shows identity and lifecycle evidence without treating replicated logon as exact activity', async () => {
    renderProfile();

    expect(await screen.findByRole('heading', { name: 'Alex Example' })).toBeDefined();
    expect(screen.getByText('a.example@corp.example')).toBeDefined();
    expect(screen.getByText(/lastLogonTimestamp is replicated and can be stale/)).toBeDefined();
    expect(screen.getByText(/does not infer ownership/)).toBeDefined();
    expect(invokeMock).toHaveBeenCalledWith('usermanagement', 'getUserProfile', {
      objectId: '00112233-4455-6677-8899-aabbccddeeff',
      connection: null,
    });
  });

  it('supports keyboard tab navigation and exposes privileged warnings with direct evidence', async () => {
    renderProfile();
    const overviewTab = await screen.findByRole('tab', { name: 'Overview' });
    overviewTab.focus();
    fireEvent.keyDown(overviewTab, { key: 'ArrowRight' });

    const accessTab = screen.getByRole('tab', { name: 'Access' });
    expect(accessTab.getAttribute('aria-selected')).toBe('true');
    expect(document.activeElement).toBe(accessTab);
    const panel = screen.getByRole('tabpanel', { name: 'Access' });
    expect(within(panel).getByText('1 privileged')).toBeDefined();
    expect(within(panel).getAllByText('Domänen-Admins').length).toBeGreaterThan(0);

    await userEvent.type(within(panel).getByRole('searchbox', { name: 'Search direct groups' }), 'GG-App');
    expect(within(panel).getByText('GG-App')).toBeDefined();
  });

  it('makes missing privileged classification coverage explicit instead of claiming no access', async () => {
    renderProfile({
      ...profile,
      access: {
        directGroups: profile.access.directGroups,
        privilegedCoverage: 'UNAVAILABLE',
        privilegedCoverageExplanation: 'The SID-validated privileged-group allowlist could not be evaluated.',
        directPrivilegedGroups: [],
      },
    });

    await userEvent.click(await screen.findByRole('tab', { name: 'Access' }));
    const panel = screen.getByRole('tabpanel', { name: 'Access' });
    expect(within(panel).getByText('Coverage unavailable')).toBeDefined();
    expect(within(panel).queryByText('None found')).toBeNull();
  });
});
