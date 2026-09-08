import type {
  ItLifecycleSettingsValue,
  NessusSettingsValue,
  OpsiSettingsValue,
} from '../../shared/api-types';
import type { SettingsSectionId } from './SettingsSectionNavigation';

export interface SettingsValidationIssue {
  field: string;
  message: string;
  section: SettingsSectionId;
  sectionLabel: string;
}

function inRange(value: number, minimum: number, maximum: number) {
  return Number.isFinite(value) && value >= minimum && value <= maximum;
}

function issue(
  section: SettingsSectionId,
  sectionLabel: string,
  field: string,
  message: string,
): SettingsValidationIssue {
  return { section, sectionLabel, field, message };
}

function normalizedThumbprintLength(value: string) {
  return (value.match(/[0-9a-f]/gi) ?? []).length;
}

function isValidHttpsUrl(value: string) {
  try {
    return new URL(value).protocol === 'https:';
  } catch {
    return false;
  }
}

export function validateItLifecycleSettings(value: ItLifecycleSettingsValue): SettingsValidationIssue[] {
  const section = 'environment-health';
  const label = 'Kaspersky';
  const issues: SettingsValidationIssue[] = [];
  if (!inRange(value.inventoryLimit, 1, 100_000)) {
    issues.push(issue(section, label, 'inventoryLimit', 'Inventory limit must be between 1 and 100000.'));
  }
  if (!inRange(value.staleWarningDays, 1, 3650)) {
    issues.push(issue(section, label, 'staleWarningDays', 'Stale warning must be between 1 and 3650 days.'));
  }
  if (!inRange(value.staleCriticalDays, 1, 3650)) {
    issues.push(issue(section, label, 'staleCriticalDays', 'Stale cleanup candidate must be between 1 and 3650 days.'));
  } else if (inRange(value.staleWarningDays, 1, 3650) && value.staleCriticalDays <= value.staleWarningDays) {
    issues.push(issue(section, label, 'staleCriticalDays', 'Stale cleanup candidate must be greater than stale warning.'));
  }
  if (!inRange(value.kaspersky.port, 1, 65_535)) {
    issues.push(issue(section, label, 'kaspersky.port', 'KSC OpenAPI port must be between 1 and 65535.'));
  }
  if (!inRange(value.kaspersky.requestTimeoutSeconds, 1, 600)) {
    issues.push(issue(section, label, 'kaspersky.requestTimeoutSeconds', 'KSC timeout must be between 1 and 600 seconds.'));
  }
  const thumbprintLength = normalizedThumbprintLength(value.kaspersky.trustedCertificateThumbprint);
  if (thumbprintLength !== 0 && thumbprintLength !== 40 && thumbprintLength !== 64) {
    issues.push(issue(section, label, 'kaspersky.trustedCertificateThumbprint', 'KSC certificate thumbprint must be empty, SHA-1, or SHA-256.'));
  }
  return issues;
}

export function validateNessusSettings(value: NessusSettingsValue): SettingsValidationIssue[] {
  const section = 'vulnerability-management';
  const label = 'Nessus';
  const issues: SettingsValidationIssue[] = [];
  if (!isValidHttpsUrl(value.serverUrl)) {
    issues.push(issue(section, label, 'serverUrl', 'Nessus HTTPS URL must be a valid HTTPS URL.'));
  }
  if (!inRange(value.requestTimeoutSeconds, 1, 600)) {
    issues.push(issue(section, label, 'requestTimeoutSeconds', 'Nessus timeout must be between 1 and 600 seconds.'));
  }
  const thumbprintLength = normalizedThumbprintLength(value.trustedCertificateThumbprint);
  if (thumbprintLength !== 0 && thumbprintLength !== 40 && thumbprintLength !== 64) {
    issues.push(issue(section, label, 'trustedCertificateThumbprint', 'Nessus certificate fingerprint must be empty, SHA-1, or SHA-256.'));
  }
  if (!inRange(value.cacheTtlMinutes, 1, 1440)) {
    issues.push(issue(section, label, 'cacheTtlMinutes', 'Cache TTL must be between 1 and 1440 minutes.'));
  }
  if (!inRange(value.backfillDays, 0, 365)) {
    issues.push(issue(section, label, 'backfillDays', 'Backfill must be between 0 and 365 days.'));
  }
  if (!inRange(value.retentionDays, 7, 3650)) {
    issues.push(issue(section, label, 'retentionDays', 'Retention must be between 7 and 3650 days.'));
  }
  if (!inRange(value.staleWarningDays, 1, 3650)) {
    issues.push(issue(section, label, 'staleWarningDays', 'Nessus stale warning must be between 1 and 3650 days.'));
  }
  if (!inRange(value.staleCriticalDays, 1, 3650)) {
    issues.push(issue(section, label, 'staleCriticalDays', 'Nessus stale critical threshold must be between 1 and 3650 days.'));
  } else if (inRange(value.staleWarningDays, 1, 3650) && value.staleCriticalDays <= value.staleWarningDays) {
    issues.push(issue(section, label, 'staleCriticalDays', 'Nessus stale critical threshold must be greater than stale warning.'));
  }
  return issues;
}

export function validateOpsiSettings(value: OpsiSettingsValue): SettingsValidationIssue[] {
  const section = 'patch-management';
  const label = 'opsi';
  const issues: SettingsValidationIssue[] = [];
  if (!inRange(value.port, 1, 65_535)) {
    issues.push(issue(section, label, 'port', 'opsi port must be between 1 and 65535.'));
  }
  if (!inRange(value.requestTimeoutSeconds, 1, 600)) {
    issues.push(issue(section, label, 'requestTimeoutSeconds', 'opsi timeout must be between 1 and 600 seconds.'));
  }
  return issues;
}
