import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { UserSummary } from '../shared/api-types';
import { TargetProvider } from '../shared/targets/TargetContext';
import { GlobalSearch } from './GlobalSearch';
import { WorkingSetProvider } from '../shared/objects/WorkingSetContext';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));
vi.mock('../shared/bridge/bridgeClient', () => ({
  invoke: invokeMock,
  invokeCancellable: (module: string, action: string) => ({ requestId: action, cancel: vi.fn(), promise: invokeMock(module, action) }),
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
  employeeId: null,
  department: 'IT',
  title: 'Administrator',
  organizationalUnitPath: 'OU=Users,DC=corp,DC=example',
  enabled: true,
  replicatedLastLogonAtUtc: null,
};

function CurrentLocation() {
  return <output aria-label="Current location">{useLocation().pathname}</output>;
}

function renderSearch(onClose = vi.fn()) {
  return {
    onClose,
    ...render(
      <MemoryRouter initialEntries={['/']}>
        <TargetProvider>
          <WorkingSetProvider>
          <GlobalSearch open onClose={onClose} />
          <Routes>
            <Route path="*" element={<CurrentLocation />} />
          </Routes>
          </WorkingSetProvider>
        </TargetProvider>
      </MemoryRouter>,
    ),
  };
}

describe('GlobalSearch', () => {
  beforeEach(() => {
    localStorage.clear();
    invokeMock.mockReset();
    invokeMock.mockImplementation((module: string, action: string) => {
      if (module === 'targets' && action === 'list') return Promise.resolve({ targets: [] });
      if (action === 'getStoredObjectLists') return Promise.resolve({ workspace: { scope: 'local', localComputerName: 'LOCAL' },
        maximumRecords: 5000, maximumSourceReads: 128, retrievedAtUtc: new Date().toISOString(), search: null, reads: [] });
      if (action === 'getCachedObjectLists') return Promise.resolve({ tenantId: '11111111-1111-1111-1111-111111111111', sessionRevision: 1,
        revision: 1, recordLimit: 5000, cachedSourceRecords: 1, loadedSourceRecords: 1, truncated: false, reads: [{
          state: { query: { resource: 'USERS', objectId: null, securityIdentifier: null }, snapshotRevision: 1, availability: 'AVAILABLE',
            retrievedAtUtc: new Date().toISOString(), lastAttemptAtUtc: new Date().toISOString(), retainedUntilUtc: new Date(Date.now() + 60000).toISOString(),
            freshUntilUtc: null, lastAttemptError: null, loadedCount: 1, declaredTotal: 1, coverage: 'RETURNED_SET' },
          rows: [{ kind: 'USER', source: 'ENTRA', objectId: alex.objectId, displayName: alex.displayName, userPrincipalName: alex.userPrincipalName,
            accountEnabled: true, operatingSystem: null, securityIdentifier: null, registrationDeviceId: null, assignedSkuIds: null }],
        }] });
      if (module === 'inventory' && action === 'listHosts') return Promise.resolve({ hosts: [] });
      if (module === 'security' && action === 'listHosts') return Promise.resolve({ hosts: [] });
      if (module === 'activedirectory' && action === 'searchComputers') return Promise.resolve({
        computers: [{
          name: 'ALEX-PC',
          dnsHostName: 'alex-pc.corp.example',
          operatingSystem: 'Windows 11 Pro',
          enabled: true,
          description: null,
          distinguishedName: null,
          lastLogonDate: null,
        }],
        truncated: false,
      });
      if (module === 'usermanagement' && action === 'listUsers') return Promise.resolve({
        domainJoined: true,
        domainName: 'corp.example',
        baseDistinguishedName: 'DC=corp,DC=example',
        page: 1,
        pageSize: 6,
        totalCount: 1,
        users: [alex],
      });
      return Promise.resolve({});
    });
  });

  it('searches the shared cached set without per-keystroke source reads and opens a scoped user by keyboard', async () => {
    const user = userEvent.setup();
    const { onClose } = renderSearch();
    const input = screen.getByRole('combobox', { name: /Search navigation/ });

    await waitFor(() => expect(document.activeElement).toBe(input));
    await user.type(input, 'Alex');
    expect(invokeMock.mock.calls.filter((call) => call[0] === 'usermanagement')).toHaveLength(0);

    expect(await screen.findByText('Alex Example')).toBeDefined();
    expect(invokeMock.mock.calls.some(call => call[0] === 'activedirectory' || call[0] === 'usermanagement' || call[1] === 'read')).toBe(false);

    await user.keyboard('{Enter}');
    expect(onClose).toHaveBeenCalledOnce();
    expect(screen.getByRole('status', { name: 'Current location' }).textContent)
      .toBe('/users/entra/11111111-1111-1111-1111-111111111111/00112233-4455-6677-8899-aabbccddeeff');
  });

  it('opens local navigation immediately and closes on Escape', async () => {
    const user = userEvent.setup();
    const { onClose } = renderSearch();
    const input = screen.getByRole('combobox', { name: /Search navigation/ });

    await user.type(input, 'configuration');
    expect(await screen.findByRole('option', { name: /Settings/ })).toBeDefined();
    await user.keyboard('{Escape}');

    expect(onClose).toHaveBeenCalledOnce();
  });
});
