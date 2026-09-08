import { describe, expect, it } from 'vitest';
import type {
  AdComputer,
  SavedTarget,
  StoredInventoryHost,
  StoredSecurityScanHost,
  UserSummary,
} from '../shared/api-types';
import { clientResults, navigationResults, savedTargetResults, userResults } from './searchResults';

const directoryComputer: AdComputer = {
  name: 'PC-42',
  dnsHostName: 'pc-42.corp.example',
  operatingSystem: 'Windows 11 Pro',
  enabled: true,
  description: null,
  distinguishedName: 'CN=PC-42,OU=Clients,DC=corp,DC=example',
  lastLogonDate: null,
};

const savedClient: SavedTarget = {
  id: 42,
  label: 'Accounting notebook',
  host: 'PC-42',
  role: 'Client',
  userName: null,
  createdAtUtc: '2026-08-27T08:00:00Z',
};

describe('global search result builders', () => {
  it('finds navigation through labels and search terms', () => {
    expect(navigationResults('configuration')).toEqual(expect.arrayContaining([
      expect.objectContaining({ label: 'Settings', to: '/settings' }),
    ]));
    expect(navigationResults('attention')).toEqual(expect.arrayContaining([
      expect.objectContaining({ label: 'Action Center', to: '/actions' }),
    ]));
    expect(navigationResults('retirement')).toEqual(expect.arrayContaining([
      expect.objectContaining({ label: 'Device Cleanup', to: '/cleanup' }),
    ]));
  });

  it('merges client evidence through a proven AD alias and keeps the result set bounded', () => {
    const inventory: StoredInventoryHost[] = [
      { host: 'PC-42', capturedAtUtc: '2026-08-27T08:00:00Z' },
      ...Array.from({ length: 8 }, (_, index) => ({
        host: `PC-${String(index + 50)}`,
        capturedAtUtc: '2026-08-27T08:00:00Z',
      })),
    ];
    const security: StoredSecurityScanHost[] = [{
      host: 'pc-42.corp.example',
      completedAtUtc: '2026-08-27T08:05:00Z',
    }];

    const results = clientResults('PC-', [directoryComputer], inventory, security, [savedClient]);

    expect(results).toHaveLength(6);
    expect(results.filter((result) => result.label === 'PC-42')).toHaveLength(1);
    expect(results.find((result) => result.label === 'PC-42')).toEqual(expect.objectContaining({
      to: '/clients/pc-42.corp.example',
      description: expect.stringContaining('Stored inventory'),
    }));
    expect(results.find((result) => result.label === 'PC-42')?.description).toContain('Stored security');
    expect(results.find((result) => result.label === 'PC-42')?.description).toContain('Saved client');
  });

  it('keeps ambiguous domain identities and complete IP addresses separate', () => {
    const directory = [
      directoryComputer,
      { ...directoryComputer, dnsHostName: 'pc-42.other.example', distinguishedName: 'CN=PC-42,DC=other,DC=example' },
    ];
    const inventory: StoredInventoryHost[] = [
      { host: 'PC-42', capturedAtUtc: '2026-08-27T08:00:00Z' },
      { host: '10.20.30.40', capturedAtUtc: '2026-08-27T08:00:00Z' },
      { host: '10.99.1.2', capturedAtUtc: '2026-08-27T08:00:00Z' },
    ];

    const results = clientResults('', directory, inventory, [], []);

    expect(results).toEqual([]);
    const named = clientResults('PC-42', directory, inventory, [], []);
    expect(named).toHaveLength(3);
    expect(named.map((result) => result.to)).toEqual(expect.arrayContaining([
      '/clients/pc-42.corp.example',
      '/clients/pc-42.other.example',
      '/clients/PC-42',
    ]));
    expect(clientResults('10.', [], inventory, [], []).map((result) => result.to)).toEqual([
      '/clients/10.20.30.40',
      '/clients/10.99.1.2',
    ]);
  });

  it('maps directory users and saved server roles to stable deep links', () => {
    const user: UserSummary = {
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
    const domainController: SavedTarget = {
      ...savedClient,
      id: 43,
      label: 'Primary DC',
      host: 'dc01.corp.example',
      role: 'DomainController',
    };

    expect(userResults([user])[0]).toEqual(expect.objectContaining({
      label: 'Alex Example',
      to: '/users/00112233-4455-6677-8899-aabbccddeeff',
    }));
    expect(savedTargetResults('primary', [domainController])[0]).toEqual(expect.objectContaining({
      to: '/activedirectory',
    }));
  });
});
