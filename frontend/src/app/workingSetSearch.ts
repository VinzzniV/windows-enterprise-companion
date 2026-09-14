import type { ObjectKind } from '../shared/api-types.generated';
import { objectPath, objectSourceLabel } from '../shared/objects/objectRoutes';
import { queryWorkingSet, type WorkingSetSnapshot } from '../shared/objects/workingSet';
import type { GlobalSearchCategory, GlobalSearchResult } from './searchResults';

const categories: Record<ObjectKind, GlobalSearchCategory> = { USER: 'Users', DEVICE: 'Devices', GROUP: 'Groups' };

export function workingSetSearch(query: string, snapshot: WorkingSetSnapshot | null): GlobalSearchResult[] {
  if (!snapshot || !query.trim()) return [];
  return (['USER', 'DEVICE', 'GROUP'] as const).flatMap(kind => queryWorkingSet(snapshot, { kind, query, page: 1, pageSize: 6 }).rows.map(row => {
    const reference = row.references[0];
    const list = kind === 'DEVICE' ? '/devices' : kind === 'USER' ? '/users/workspace' : '/groups/workspace';
    const ambiguous = row.duplicateSourceIdentity || row.conflictingIdentityEvidence;
    return { id: row.key, category: categories[kind], label: row.label,
      description: `${[...new Set(row.observations.map(observation => objectSourceLabel[observation.source]))].join(' / ')} · ${reference?.scope ?? 'Scope unavailable'}${row.hasCandidates || ambiguous ? ' · Candidate / conflicting evidence' : ''}`,
      to: reference && !ambiguous ? objectPath(reference) : `${list}?q=${encodeURIComponent(row.label)}` };
  }));
}
