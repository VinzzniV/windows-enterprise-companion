import { describe, expect, it } from 'vitest';
import type { AdComputer, SavedTarget, StoredInventoryHost, StoredSecurityScanHost } from '../../shared/api-types';
import {
  buildClientList,
  filterClients,
  groupClients,
  isLocalClient,
  siteOf,
  toClientTarget,
} from './clients';

const ad = (over: Partial<AdComputer>): AdComputer => ({
  name: 'PC1',
  dnsHostName: null,
  operatingSystem: null,
  enabled: true,
  description: null,
  distinguishedName: null,
  lastLogonDate: null,
  ...over,
});

describe('siteOf', () => {
  it('takes the prefix before the first dash', () => {
    expect(siteOf('KF-PC001')).toBe('KF');
    expect(siteOf('pk-netprt01')).toBe('PK');
  });
  it('is "Other" without a leading site code', () => {
    expect(siteOf('PC001')).toBe('Other');
    expect(siteOf('-x')).toBe('Other');
  });
});

describe('buildClientList', () => {
  it('preserves domain, short-name and IP targets independently', () => {
    const list = buildClientList([
      ad({ name: 'PC01', dnsHostName: 'pc01.a.example' }),
      ad({ name: 'PC01', dnsHostName: 'pc01.b.example' }),
    ], [
      { host: 'PC01', capturedAtUtc: '2026-09-14T08:00:00Z' },
      { host: '10.1.2.3', capturedAtUtc: '2026-09-14T08:00:00Z' },
      { host: '10.8.9.10', capturedAtUtc: '2026-09-14T08:00:00Z' },
    ], []);
    expect(list).toHaveLength(5);
    expect(list.filter((client) => client.inAd).every((client) => !client.scanned)).toBe(true);
    expect(new Set(list.map((client) => client.key)).size).toBe(5);
  });
  it('merges AD, scan history and saved targets into one entry per host', () => {
    const adComputers = [ad({ name: 'KF-PC1', dnsHostName: 'kf-pc1.corp.local', operatingSystem: 'Windows 11' })];
    const scanned: StoredInventoryHost[] = [{ host: 'KF-PC1.corp.local', capturedAtUtc: '2026-07-06T08:00:00Z' }];
    const security: StoredSecurityScanHost[] = [{ host: 'kf-pc1.corp.local', completedAtUtc: '2026-07-06T09:00:00Z' }];
    const saved: SavedTarget[] = [
      { id: 1, label: 'KF-PC1', host: 'kf-pc1.corp.local', role: 'Client', userName: null, createdAtUtc: 'x' },
    ];

    const list = buildClientList(adComputers, scanned, saved, security);
    expect(list).toHaveLength(1);
    expect(list[0]).toMatchObject({
      host: 'kf-pc1.corp.local', // FQDN kept for scanning
      name: 'KF-PC1',
      os: 'Windows 11',
      scanned: true,
      saved: true,
      inAd: true,
      capturedAtUtc: '2026-07-06T08:00:00Z',
      securityScanned: true,
      securityCompletedAtUtc: '2026-07-06T09:00:00Z',
    });
  });

  it('keeps scanned-only and saved-only hosts that are not in AD', () => {
    const list = buildClientList(
      [],
      [{ host: 'OLD-PC', capturedAtUtc: '2026-07-01T00:00:00Z' }],
      [{ id: 2, label: 'opsi', host: 'opsi.corp.local', role: 'OpsiServer', userName: null, createdAtUtc: 'x' }],
      [{ host: 'SECURITY-ONLY', completedAtUtc: '2026-07-02T00:00:00Z' }],
    );
    expect(list.map((c) => c.name).sort()).toEqual(['OLD-PC', 'SECURITY-ONLY', 'opsi']);
    expect(list.find((c) => c.name === 'OLD-PC')).toMatchObject({ scanned: true, inAd: false });
    expect(list.find((c) => c.name === 'SECURITY-ONLY')).toMatchObject({
      securityScanned: true,
      securityCompletedAtUtc: '2026-07-02T00:00:00Z',
      inAd: false,
    });
  });
});

describe('filterClients', () => {
  const clients = buildClientList(
    [ad({ name: 'KF-PC1', operatingSystem: 'Windows 11' }), ad({ name: 'PK-PC2', operatingSystem: 'Windows 10' })],
    [],
    [],
  );
  it('matches name, host and os case-insensitively', () => {
    expect(filterClients(clients, 'kf').map((c) => c.name)).toEqual(['KF-PC1']);
    expect(filterClients(clients, 'windows 10').map((c) => c.name)).toEqual(['PK-PC2']);
    expect(filterClients(clients, '')).toHaveLength(2);
  });
});

describe('isLocalClient / toClientTarget', () => {
  it('treats this machine (short name, any case) as local → null target', () => {
    expect(isLocalClient('DESKTOP-1', 'desktop-1')).toBe(true);
    expect(isLocalClient('desktop-1.corp.local', 'DESKTOP-1')).toBe(false);
    expect(toClientTarget('DESKTOP-1', 'desktop-1', undefined)).toBeNull();
  });

  it('remote host with no credentials scans as the current user', () => {
    expect(toClientTarget('pc1.corp.local', 'DESKTOP-1', undefined)).toEqual({ host: 'pc1.corp.local' });
    expect(toClientTarget('pc1', 'DESKTOP-1', { userName: '  ', domain: '', password: '' })).toEqual({
      host: 'pc1',
    });
  });

  it('remote host carries explicit session credentials, password never dropped', () => {
    expect(
      toClientTarget('pc1', 'DESKTOP-1', { userName: ' admin ', domain: ' CORP ', password: 'secret' }),
    ).toEqual({ host: 'pc1', userName: 'admin', domain: 'CORP', password: 'secret' });
    expect(
      toClientTarget('pc1', 'DESKTOP-1', { userName: 'admin', domain: '', password: 'x' }),
    ).toMatchObject({ domain: null });
  });
});

describe('groupClients', () => {
  const clients = buildClientList(
    [
      ad({ name: 'KF-PC1', operatingSystem: 'Windows 11' }),
      ad({ name: 'KF-PC2', operatingSystem: 'Windows 10' }),
      ad({ name: 'PK-PC3', operatingSystem: 'Windows 11' }),
    ],
    [],
    [],
  );
  it('groups by site', () => {
    const groups = groupClients(clients, 'site');
    expect(groups.map((g) => g.label)).toEqual(['KF', 'PK']);
    expect(groups[0].clients).toHaveLength(2);
  });
  it('groups by os', () => {
    const groups = groupClients(clients, 'os');
    expect(groups.map((g) => g.label)).toEqual(['Windows 10', 'Windows 11']);
  });
  it('none yields a single unlabeled group', () => {
    expect(groupClients(clients, 'none')).toHaveLength(1);
  });
});
