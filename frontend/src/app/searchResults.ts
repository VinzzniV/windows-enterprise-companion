import type {
  AdComputer,
  SavedTarget,
  StoredInventoryHost,
  StoredSecurityScanHost,
  UserSummary,
} from '../shared/api-types';
import { navigationGroups } from './routeRegistry';
import { hostAddressKey as clientKey } from '../shared/targets/hostAddress';

export type GlobalSearchCategory = 'Navigation' | 'Users' | 'Clients' | 'Saved targets';

export interface GlobalSearchResult {
  id: string;
  category: GlobalSearchCategory;
  label: string;
  description: string;
  to: string;
}

function matches(query: string, ...values: (string | null | undefined)[]): boolean {
  const normalized = query.trim().toLocaleLowerCase();
  return normalized === '' || values.some((value) => value?.toLocaleLowerCase().includes(normalized));
}

export function navigationResults(query: string): GlobalSearchResult[] {
  return navigationGroups.flatMap((group) => group.items)
    .filter((item) => matches(query, item.label, ...item.searchTerms))
    .map((item) => ({
      id: `navigation:${item.to}`,
      category: 'Navigation' as const,
      label: item.label,
      description: 'Open workspace',
      to: item.to,
    }));
}

function savedTargetDestination(target: SavedTarget): string {
  if (target.role === 'DomainController') return '/activedirectory';
  if (target.role === 'OpsiServer') return '/patchmanagement';
  if (target.role === 'PrintServer') return '/printmanagement';
  return `/clients/${encodeURIComponent(target.host)}`;
}

export function savedTargetResults(query: string, targets: readonly SavedTarget[]): GlobalSearchResult[] {
  return targets
    .filter((target) => matches(query, target.label, target.host, target.role))
    .slice(0, 6)
    .map((target) => ({
      id: `saved:${target.id}`,
      category: 'Saved targets' as const,
      label: target.label,
      description: `${target.role} · ${target.host}`,
      to: savedTargetDestination(target),
    }));
}

interface ClientCandidate {
  host: string;
  name: string;
  details: Set<string>;
}

export function clientResults(
  query: string,
  directory: readonly AdComputer[],
  inventory: readonly StoredInventoryHost[],
  security: readonly StoredSecurityScanHost[],
  savedTargets: readonly SavedTarget[],
): GlobalSearchResult[] {
  if (query.trim() === '') return [];
  const candidates = new Map<string, ClientCandidate>();
  const ensure = (host: string, name: string) => {
    const key = clientKey(host);
    const existing = candidates.get(key);
    if (existing) return existing;
    const created: ClientCandidate = { host, name, details: new Set() };
    candidates.set(key, created);
    return created;
  };

  for (const computer of directory) {
    const candidate = ensure(computer.dnsHostName ?? computer.name, computer.name);
    candidate.details.add(computer.enabled === false ? 'AD disabled'
      : computer.enabled === true ? 'Active Directory' : 'AD account state unknown');
    if (computer.operatingSystem) candidate.details.add(computer.operatingSystem);
  }
  for (const stored of inventory) ensure(stored.host, stored.host).details.add('Stored inventory');
  for (const stored of security) ensure(stored.host, stored.host).details.add('Stored security');
  for (const target of savedTargets.filter((target) => target.role === 'Client')) {
    ensure(target.host, target.label || target.host).details.add('Saved client');
  }

  return [...candidates.values()]
    .filter((candidate) => matches(query, candidate.name, candidate.host, ...candidate.details))
    .sort((left, right) => left.name.localeCompare(right.name))
    .slice(0, 6)
    .map((candidate) => ({
      id: `client:${clientKey(candidate.host)}`,
      category: 'Clients' as const,
      label: candidate.name,
      description: `${candidate.host} · ${[...candidate.details].join(' · ') || 'Client'}`,
      to: `/clients/${encodeURIComponent(candidate.host)}`,
    }));
}

export function userResults(users: readonly UserSummary[]): GlobalSearchResult[] {
  return users.slice(0, 6).map((user) => ({
    id: `user:${user.objectId}`,
    category: 'Users' as const,
    label: user.displayName,
    description: [user.samAccountName, user.department, user.title].filter(Boolean).join(' · ') || 'Directory user',
    to: `/users/${encodeURIComponent(user.objectId)}`,
  }));
}
