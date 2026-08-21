import { describe, expect, it } from 'vitest';
import {
  validateItLifecycleSettings,
  validateNessusSettings,
  validateOpsiSettings,
} from './settingsValidation';

const lifecycle = {
  inventoryLimit: 10000,
  staleWarningDays: 60,
  staleCriticalDays: 90,
  targetAgentVersion: '',
  targetKesVersion: '',
  kaspersky: {
    server: '', port: 13299, requestTimeoutSeconds: 60,
    trustedCertificateThumbprint: '', excludedAdministrationGroups: [],
  },
};

const nessus = {
  serverUrl: 'https://nessus.example.test:8834', requestTimeoutSeconds: 120,
  trustedCertificateThumbprint: '', cacheTtlMinutes: 15, backfillDays: 90,
  retentionDays: 365, staleWarningDays: 14, staleCriticalDays: 30,
  excludedScanIds: [], missingNessusExcludedOuPatterns: [], missingNessusExcludedHostPatterns: [],
};

describe('settings validation', () => {
  it('accepts values at the existing backend boundaries', () => {
    expect(validateItLifecycleSettings(lifecycle)).toEqual([]);
    expect(validateNessusSettings(nessus)).toEqual([]);
    expect(validateOpsiSettings({ server: '', port: 1, requestTimeoutSeconds: 600, defaultDepotFilter: '', trustServerCertificate: false })).toEqual([]);
  });

  it('reports every invalid Environment Health boundary', () => {
    const issues = validateItLifecycleSettings({
      ...lifecycle,
      inventoryLimit: 0,
      staleWarningDays: 0,
      staleCriticalDays: 3651,
      kaspersky: { ...lifecycle.kaspersky, port: 0, requestTimeoutSeconds: 601, trustedCertificateThumbprint: 'A' },
    });
    expect(issues.map((entry) => entry.field)).toEqual([
      'inventoryLimit', 'staleWarningDays', 'staleCriticalDays', 'kaspersky.port',
      'kaspersky.requestTimeoutSeconds', 'kaspersky.trustedCertificateThumbprint',
    ]);
  });

  it('reports every invalid Nessus and opsi boundary', () => {
    const nessusIssues = validateNessusSettings({
      ...nessus,
      serverUrl: 'http://nessus.example.test', requestTimeoutSeconds: 0,
      trustedCertificateThumbprint: 'A', cacheTtlMinutes: 0, backfillDays: 366,
      retentionDays: 6, staleWarningDays: 0, staleCriticalDays: 3651,
    });
    expect(nessusIssues).toHaveLength(8);
    expect(validateOpsiSettings({ server: '', port: 0, requestTimeoutSeconds: 601, defaultDepotFilter: '', trustServerCertificate: false })).toHaveLength(2);
  });
});
