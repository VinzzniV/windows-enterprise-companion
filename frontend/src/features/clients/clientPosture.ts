export const clientPostureFilters = [
  'ALL',
  'HEALTHY',
  'PROBLEMS',
  'INCOMPLETE',
  'UNMANAGED',
  'STALE',
  'OUTDATED',
  'MISSING_KASPERSKY',
  'ORPHAN_KASPERSKY',
  'MISSING_OPSI',
  'ORPHAN_OPSI',
  'MISSING_NESSUS',
  'STALE_NESSUS',
  'NESSUS_CRITICAL',
  'NESSUS_HIGH',
] as const;

export type ClientPostureFilter = typeof clientPostureFilters[number];

export const clientPostureLabels: Record<ClientPostureFilter, string> = {
  ALL: 'All postures',
  HEALTHY: 'Healthy',
  PROBLEMS: 'Problems',
  INCOMPLETE: 'Incomplete',
  UNMANAGED: 'Unmanaged',
  STALE: 'Stale',
  OUTDATED: 'Outdated',
  MISSING_KASPERSKY: 'Missing Kaspersky',
  ORPHAN_KASPERSKY: 'Orphan Kaspersky',
  MISSING_OPSI: 'Missing opsi',
  ORPHAN_OPSI: 'Orphan opsi',
  MISSING_NESSUS: 'Missing Nessus',
  STALE_NESSUS: 'Stale Nessus',
  NESSUS_CRITICAL: 'Nessus Critical',
  NESSUS_HIGH: 'Nessus High',
};

export function clientPostureFilterFromUrl(value: string | null): ClientPostureFilter {
  const normalized = value?.trim().toUpperCase();
  return clientPostureFilters.includes(normalized as ClientPostureFilter)
    ? normalized as ClientPostureFilter
    : 'ALL';
}
