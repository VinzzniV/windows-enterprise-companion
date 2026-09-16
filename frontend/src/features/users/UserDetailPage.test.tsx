import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
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
    directoryScope: null,
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
      evaluatedDeviceCount: 0, workingSetTruncated: false, multipleLatestSnapshotDeviceCount: 0,
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

const profileWithDevice: UserProfileResult = {
  ...profile,
  devices: {
    coverage: 'PARTIAL',
    explanation: 'Some stored devices have missing Inventory user evidence.',
    sourceCoverage: {
      storedDeviceCount: 12,
      evaluatedDeviceCount: 12, workingSetTruncated: false, multipleLatestSnapshotDeviceCount: 0,
      evidenceCapturedDeviceCount: 8,
      notCapturedDeviceCount: 3,
      unavailableDeviceCount: 1,
      truncatedDeviceCount: 0,
    },
    totalLinkedDeviceCount: 12,
    linkedDevicesTruncated: true,
    linkedDevices: [{
      host: 'PC-42',
      inventoryCapturedAtUtc: '2026-08-27T08:00:00Z',
      relationshipEvidence: [{
        relationshipType: 'LAST_INTERACTIVE_USER',
        source: 'WEC Inventory',
        observedAtUtc: '2026-08-27T08:00:00Z',
        confidence: 'HIGH',
        explanation: 'The SID resolved from the last interactive domain user.',
        profileLastUseAtUtc: null,
      }],
      software: {
        isAvailable: true,
        isComplete: true,
        capturedAtUtc: '2026-08-27T08:00:00Z',
        installedCount: 42,
        sample: [{ name: 'Admin Tool', version: '2.0', publisher: 'Example' }],
        explanation: 'Complete stored capture.',
      },
      health: {
        isAvailable: true,
        isComplete: false,
        capturedAtUtc: '2026-08-27T08:30:00Z',
        criticalCount: 1,
        warningCount: 2,
        unknownCount: 1,
        healthyCount: 3,
        explanation: 'Three of four checks observed.',
      },
      security: {
        isAvailable: true,
        isComplete: true,
        capturedAtUtc: '2026-08-27T08:45:00Z',
        scanStatus: 'Completed',
        criticalCount: 0,
        highCount: 2,
        mediumCount: 3,
        lowCount: 4,
        explanation: 'All checks succeeded.',
      },
      vulnerabilities: {
        availability: 'AVAILABLE',
        deviceMatched: true,
        capturedAtUtc: '2026-08-27T07:00:00Z',
        criticalCount: 1,
        highCount: 2,
        mediumCount: 3,
        lowCount: 4,
        explanation: 'Stored Nessus match.',
      },
    }],
  },
};

function ResolvedRoute() {
  const location = useLocation();
  return <output data-testid="resolved-route">{location.pathname}{location.search} · {JSON.stringify(location.state)}</output>;
}

function renderProfile(value: UserProfileResult = profile, section = '') {
  invokeMock.mockImplementation((module: string, action: string) => {
    if (module === 'targets' && action === 'list') return Promise.resolve({ targets: [] });
    if (module === 'usermanagement' && action === 'getUserProfile') return Promise.resolve(value);
    if (module === 'usermanagement' && action === 'exportLeaverReview') {
      return Promise.resolve({ cancelled: false, filePath: 'C:\\temp\\leaver-review.md' });
    }
    return Promise.resolve({});
  });
  return render(
    <MemoryRouter initialEntries={['/users/00112233-4455-6677-8899-aabbccddeeff' + section]}>
      <TargetProvider>
        <Routes>
          <Route path="/users/:objectId" element={<UserDetailPage />} />
          <Route path="/users/ad/:scope/:objectId" element={<ResolvedRoute />} />
        </Routes>
      </TargetProvider>
    </MemoryRouter>,
  );
}

describe('UserDetailPage', () => {
  it('resolves a legacy deep link through the verified directory scope and retains its section', async () => {
    renderProfile({ ...profile, identity: { ...profile.identity, directoryScope: 'verified.example' } }, '?section=leaver');
    const route = await screen.findByTestId('resolved-route');
    expect(route.textContent).toContain('/users/ad/verified.example/00112233-4455-6677-8899-aabbccddeeff?section=leaver');
    expect(route.textContent).toContain('resolveLegacyUser');
    expect(route.textContent).not.toContain('corp.example');
  });

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

  it('shows bounded relationship evidence and stored device context without starting scans', async () => {
    renderProfile(profileWithDevice);
    await userEvent.click(await screen.findByRole('tab', { name: 'Devices' }));

    const panel = screen.getByRole('tabpanel', { name: 'Devices' });
    expect(within(panel).getByRole('heading', { name: 'User relationships' })).toBeDefined();
    expect(within(panel).getByText(/Showing 1 of 12 linked devices/)).toBeDefined();
    expect(within(panel).getByText(/The SID resolved from the last interactive domain user/)).toBeDefined();
    const softwarePanel = within(panel).getByRole('heading', { name: 'Installed software' }).closest('section');
    expect(softwarePanel?.textContent).toContain('42 applications');
    expect(within(panel).getByText('1 critical · 2 warning')).toBeDefined();
    expect(within(panel).getByRole('link', { name: 'Software & inventory' }).getAttribute('href'))
      .toBe('/clients/PC-42?section=inventory');
    expect(within(panel).getByRole('link', { name: 'Vulnerabilities' }).getAttribute('href'))
      .toContain('/vulnerabilities?tab=findings&asset=PC-42');
    expect(invokeMock.mock.calls.map(([module, action]) => `${module}/${action}`).sort())
      .toEqual(['targets/list', 'usermanagement/getUserProfile'].sort());
  });

  it('states when no exact SID-matched device relationship exists', async () => {
    renderProfile();
    await userEvent.click(await screen.findByRole('tab', { name: 'Devices' }));

    const panel = screen.getByRole('tabpanel', { name: 'Devices' });
    expect(within(panel).getByText('No linked devices')).toBeDefined();
    expect(within(panel).getByText(/does not infer device ownership/)).toBeDefined();
  });

  it('opens a read-only leaver assessment from the selected user without invoking write actions', async () => {
    renderProfile(profileWithDevice);

    await userEvent.click(await screen.findByRole('button', { name: 'Start Leaver review' }));

    const panel = screen.getByRole('tabpanel', { name: 'Leaver review' });
    expect(within(panel).getByRole('heading', { name: 'Read-only Leaver review' })).toBeDefined();
    expect(within(panel).getByText('The AD account remains enabled.')).toBeDefined();
    expect(within(panel).getByText('1 direct privileged membership remains.')).toBeDefined();
    expect(within(panel).getByRole('link', { name: 'PC-42' }).getAttribute('href')).toBe('/clients/PC-42');
    expect(within(panel).getByText('Return unresolved')).toBeDefined();
    expect(within(panel).queryByRole('button', { name: /disable|delete|remove/i })).toBeNull();
    expect(invokeMock.mock.calls.map(([module, action]) => `${module}/${action}`).sort())
      .toEqual(['targets/list', 'usermanagement/getUserProfile'].sort());
  });

  it('exports only the current session checklist and reports the selected path', async () => {
    renderProfile(profileWithDevice);
    await userEvent.click(await screen.findByRole('button', { name: 'Start Leaver review' }));
    const panel = screen.getByRole('tabpanel', { name: 'Leaver review' });

    await userEvent.click(within(panel).getAllByRole('checkbox', { name: 'Reviewed in this session' })[0]);
    expect(within(panel).getByText('1 of 6 evidence items reviewed in this session')).toBeDefined();
    await userEvent.click(within(panel).getByRole('button', { name: 'Export Markdown checklist' }));

    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith(
      'usermanagement',
      'exportLeaverReview',
      expect.objectContaining({ markdown: expect.stringContaining('- [x] Directory account state — attention') }),
    ));
    expect(await within(panel).findByText('Exported to C:\\temp\\leaver-review.md')).toBeDefined();
    const exportCall = invokeMock.mock.calls.find(([, action]) => action === 'exportLeaverReview');
    expect(exportCall?.[2].markdown).toContain('- [ ] PC-42 — RETURN UNRESOLVED');
    expect(exportCall?.[2].markdown).toContain('WEC persists no workflow state');
  });
});
